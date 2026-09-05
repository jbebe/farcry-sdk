// Far Cry 2's own developer console, driven from code instead of from the `~` prompt.
//
// Two calls and two globals, named through the address library rather than a byte pattern because
// each is a function start or a global - the case the library is keyed for. The string layout, the
// narrow ExecuteString overload and what the developer flag gates are all in
// docs/docs/engine-internals/developer-console.md.
#include "engine/console.h"

#include "engine/game_thread.h"
#include "fcse_api.h"

#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace {
    // MSVC's std::string as this build lays it out.
    struct NarrowString {
        const void* proxy;
        char buffer[16];
        uint32_t size;
        uint32_t capacity;
    };
    static_assert(sizeof(NarrowString) == 28, "the engine reads this as its own std::string");

    constexpr size_t kInlineCapacity = sizeof(NarrowString::buffer) - 1;

    constexpr ptrdiff_t kDeveloperFlagOffset = 0x68;

    using ExecuteStringFn = void(__thiscall*)(void* console, const NarrowString* line);
    using PrintLineFn = void(__cdecl*)(void* console, char developerOnly, const char* format, ...);

    FCSE::Relocation<ExecuteStringFn> g_executeString{FCSE::Uplay(0x00297D10)};
    FCSE::Relocation<PrintLineFn> g_printLine{FCSE::Uplay(0x002956F0)};

    void** g_console = nullptr;
    const void* g_stringProxy = nullptr;
    bool g_installed = false;

    // Raises the console's developer flag for as long as it is in scope, so a developer-only
    // command is found rather than answered as unknown.
    struct DeveloperFlag {
        uint8_t* flag;
        uint8_t previous;

        explicit DeveloperFlag(void* console)
            : flag(static_cast<uint8_t*>(console) + kDeveloperFlagOffset), previous(*flag) {
            *flag = 1;
        }

        ~DeveloperFlag() { *flag = previous; }
    };

    // Lays `text` out as a string the engine can read. Anything past the inline buffer lives in
    // `spill`, which the caller keeps alive for the call.
    void MakeString(NarrowString* out, const char* text, std::string* spill) {
        size_t length = std::strlen(text);

        std::memset(out, 0, sizeof(*out));
        out->proxy = g_stringProxy;
        out->size = static_cast<uint32_t>(length);

        if (length <= kInlineCapacity) {
            std::memcpy(out->buffer, text, length + 1);
            out->capacity = kInlineCapacity;
            return;
        }

        spill->assign(text, length);
        *reinterpret_cast<char**>(out->buffer) = spill->data();
        out->capacity = static_cast<uint32_t>(length);
    }
}

namespace DevTools::Console {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    g_console = FCSE::Data<void*>(FCSE::Uplay(0x01606280));
    g_stringProxy = FCSE::Data<const void>(FCSE::Uplay(0x00FD42D1));

    const char* missing = nullptr;
    if (g_console == nullptr || g_stringProxy == nullptr) {
        missing = "the console singleton";
    } else if (!g_executeString) {
        missing = "CXConsole::ExecuteString";
    } else if (!g_printLine) {
        missing = "the console's PrintLine";
    }

    if (missing != nullptr) {
        char line[192];
        std::snprintf(line, sizeof(line),
                      "console: %s was not found in this build - the command API is disabled",
                      missing);
        api->Log(line);
        return;
    }

    g_installed = true;
    api->Log("console: command API ready");
}

bool IsReady() { return g_installed && *g_console != nullptr && GameThread::IsCurrent(); }

bool Execute(const char* line) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!IsReady()) {
        api->Log("console: not ready - the line was not run");
        return false;
    }

    char logged[256];
    std::snprintf(logged, sizeof(logged), "console: > %s", line);
    api->Log(logged);

    NarrowString text;
    std::string spill;
    MakeString(&text, line, &spill);

    DeveloperFlag raised(*g_console);
    g_executeString(*g_console, &text);
    return true;
}

void Print(const char* format, ...) {
    if (!IsReady()) {
        return;
    }

    char text[512];
    va_list args;
    va_start(args, format);
    std::vsnprintf(text, sizeof(text), format, args);
    va_end(args);

    // Formatted here and handed over as a plain string, so a caller's text can never be read as a
    // format by the engine's own printf.
    g_printLine(*g_console, 0, "%s", text);
}

void PostLine(const char* line) {
    GameThread::Post([text = std::string(line)] { Execute(text.c_str()); });
}

}
