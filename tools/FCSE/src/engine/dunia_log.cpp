#include "engine/dunia_log.h"

#include "api/hook.h"
#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/dunia_api.h"
#include "log.h"
#include "ui/engine_page_abi.h"
#include "util/pe_image.h"
#include "util/text.h"
#include "util/win_string.h"

#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <windows.h>

namespace FCSE {

namespace {

    // Logs `text` as one line per '\n', all under `tag`.
    void EngineLines(const char* tag, const std::string& text) {
        for (const std::string& line : SplitLines(text)) {
            Log::Engine(tag, line);
        }
    }

    using AddLineFn = void(__fastcall*)(void* console, void* edx, const WideString* line);
    AddLineFn g_addLine = nullptr;

    void __fastcall AddLineDetour(void* console, void* edx, const WideString* line) {
        EngineLines("console", Utf8(line->Text(), line->size));
        g_addLine(console, edx, line);
    }

    using OnScriptErrorFn = void(__fastcall*)(void* scripts, void* edx, const char* source,
                                              const char* function, int line, const char* message);
    OnScriptErrorFn g_onScriptError = nullptr;

    void __fastcall OnScriptErrorDetour(void* scripts, void* edx, const char* source,
                                        const char* function, int line, const char* message) {
        std::string text = message != nullptr ? message : "Unknown error";
        if (function != nullptr) {
            text += " - in function ";
            text += function;
        }
        if (source != nullptr) {
            text += " (";
            text += source;
            text += ":";
            text += std::to_string(line);
            text += ")";
        }
        EngineLines("lua", text);
        g_onScriptError(scripts, edx, source, function, line, message);
    }

    // System:Log and System:LogToConsole: __thiscall(scriptObject, CFunctionHandler*), whose
    // retail bodies only check the argument count.
    using SystemLogFn = int(__fastcall*)(void* system, void* edx, void* handler);
    SystemLogFn g_systemLog = nullptr;
    SystemLogFn g_systemLogToConsole = nullptr;

    int(__fastcall* g_getParamCount)(void* handler) = nullptr;
    const char*(__cdecl* g_luaToString)(void* state, int index) = nullptr;

    // The handler's lua_State sits at +4, and argument N at stack index N + 1.
    constexpr ptrdiff_t kHandlerStateOffset = 4;

    void LogScriptArgument(void* handler) {
        if (g_getParamCount(handler) != 1) {
            return;
        }
        void* state = *reinterpret_cast<void**>(static_cast<uint8_t*>(handler) +
                                                kHandlerStateOffset);
        if (const char* text = g_luaToString(state, 2)) {
            EngineLines("script", text);
        }
    }

    int __fastcall SystemLogDetour(void* system, void* edx, void* handler) {
        LogScriptArgument(handler);
        return g_systemLog(system, edx, handler);
    }

    int __fastcall SystemLogToConsoleDetour(void* system, void* edx, void* handler) {
        LogScriptArgument(handler);
        return g_systemLogToConsole(system, edx, handler);
    }

    constexpr char kCrtModule[] = "msvcr80.dll";

    // sizeof(FILE) in this C runtime; __iob_func() returns stdin, stdout, stderr back to back.
    constexpr size_t kFileSize = 32;

    // A byte stream the engine writes in fragments; `file` is its FILE* for the two the C runtime
    // owns.
    struct Stream {
        const char* tag;
        const void* file = nullptr;
        std::string pending;
    };

    Stream g_streams[] = {{"stdout"}, {"stderr"}, {"debug"}};
    Stream& g_stdout = g_streams[0];
    Stream& g_stderr = g_streams[1];
    Stream& g_debug = g_streams[2];
    SRWLOCK g_streamLock = SRWLOCK_INIT;

    Stream* StreamFor(const void* file) {
        for (Stream& stream : g_streams) {
            if (stream.file != nullptr && file == stream.file) {
                return &stream;
            }
        }
        return nullptr;
    }

    // Appends to the stream's partial line and logs every line that completes.
    void Capture(Stream& stream, const char* data, size_t size) {
        std::string completed;
        AcquireSRWLockExclusive(&g_streamLock);
        stream.pending.append(data, size);
        const size_t lastNewline = stream.pending.rfind('\n');
        if (lastNewline != std::string::npos) {
            completed = stream.pending.substr(0, lastNewline + 1);
            stream.pending.erase(0, lastNewline + 1);
        }
        ReleaseSRWLockExclusive(&g_streamLock);
        if (!completed.empty()) {
            EngineLines(stream.tag, completed);
        }
    }

    int CaptureFormatted(Stream& stream, const char* format, va_list args) {
        va_list measure;
        va_copy(measure, args);
        const int length = std::vsnprintf(nullptr, 0, format, measure);
        va_end(measure);
        if (length <= 0) {
            return length;
        }
        std::string text(static_cast<size_t>(length) + 1, '\0');
        std::vsnprintf(text.data(), text.size(), format, args);
        Capture(stream, text.data(), static_cast<size_t>(length));
        return length;
    }

    int(__cdecl* g_fputs)(const char*, void*) = nullptr;
    int(__cdecl* g_fputc)(int, void*) = nullptr;
    size_t(__cdecl* g_fwrite)(const void*, size_t, size_t, void*) = nullptr;
    int(__cdecl* g_printf)(const char*, ...) = nullptr;
    int(__cdecl* g_fprintf)(void*, const char*, ...) = nullptr;
    int(__cdecl* g_vfprintf)(void*, const char*, va_list) = nullptr;
    void(__stdcall* g_outputDebugString)(const char*) = nullptr;

    int __cdecl FputsDetour(const char* text, void* file) {
        if (Stream* stream = StreamFor(file)) {
            Capture(*stream, text, std::strlen(text));
            return 0;
        }
        return g_fputs(text, file);
    }

    int __cdecl FputcDetour(int character, void* file) {
        if (Stream* stream = StreamFor(file)) {
            const char byte = static_cast<char>(character);
            Capture(*stream, &byte, 1);
            return character;
        }
        return g_fputc(character, file);
    }

    size_t __cdecl FwriteDetour(const void* data, size_t size, size_t count, void* file) {
        if (Stream* stream = StreamFor(file)) {
            Capture(*stream, static_cast<const char*>(data), size * count);
            return count;
        }
        return g_fwrite(data, size, count, file);
    }

    int __cdecl PrintfDetour(const char* format, ...) {
        va_list args;
        va_start(args, format);
        const int length = CaptureFormatted(g_stdout, format, args);
        va_end(args);
        return length;
    }

    int __cdecl VfprintfDetour(void* file, const char* format, va_list args) {
        if (Stream* stream = StreamFor(file)) {
            return CaptureFormatted(*stream, format, args);
        }
        return g_vfprintf(file, format, args);
    }

    int __cdecl FprintfDetour(void* file, const char* format, ...) {
        va_list args;
        va_start(args, format);
        const int length = VfprintfDetour(file, format, args);
        va_end(args);
        return length;
    }

    void __stdcall OutputDebugStringDetour(const char* text) {
        if (text != nullptr) {
            Capture(g_debug, text, std::strlen(text));
        }
        g_outputDebugString(text);
    }

    // One import of Dunia.dll's, the detour that replaces it and the global that keeps the real one.
    struct Import {
        const char* module;
        const char* name;
        void* detour;
        void** original;
        uintptr_t* slot = nullptr;
    };

    Import g_imports[] = {
        {kCrtModule, "fputs", reinterpret_cast<void*>(&FputsDetour),
         reinterpret_cast<void**>(&g_fputs)},
        {kCrtModule, "fputc", reinterpret_cast<void*>(&FputcDetour),
         reinterpret_cast<void**>(&g_fputc)},
        {kCrtModule, "fwrite", reinterpret_cast<void*>(&FwriteDetour),
         reinterpret_cast<void**>(&g_fwrite)},
        {kCrtModule, "printf", reinterpret_cast<void*>(&PrintfDetour),
         reinterpret_cast<void**>(&g_printf)},
        {kCrtModule, "fprintf", reinterpret_cast<void*>(&FprintfDetour),
         reinterpret_cast<void**>(&g_fprintf)},
        {kCrtModule, "vfprintf", reinterpret_cast<void*>(&VfprintfDetour),
         reinterpret_cast<void**>(&g_vfprintf)},
        {"kernel32.dll", "OutputDebugStringA", reinterpret_cast<void*>(&OutputDebugStringDetour),
         reinterpret_cast<void**>(&g_outputDebugString)},
    };

    void InstallImports() {
        const uintptr_t base = DuniaApi::Base();

        uintptr_t* iob = FindImportSlot(base, kCrtModule, "__iob_func");
        if (iob == nullptr || *iob == 0) {
            Log::Loader("Dunia.log: Dunia.dll does not import msvcr80!__iob_func - stdout and "
                        "stderr are not captured");
        } else {
            const auto* files =
                static_cast<const uint8_t*>(reinterpret_cast<void*(__cdecl*)()>(*iob)());
            g_stdout.file = files + kFileSize;
            g_stderr.file = files + 2 * kFileSize;
        }

        // Each import stands alone: a build that never imports one simply never calls it.
        std::string captured;
        for (Import& import : g_imports) {
            import.slot = FindImportSlot(base, import.module, import.name);
            if (import.slot == nullptr || *import.slot == 0) {
                Log::Loader(std::string("Dunia.log: this Dunia.dll does not import ") +
                            import.name + ", so nothing reaches it");
                continue;
            }
            *import.original = reinterpret_cast<void*>(*import.slot);
            if (!WriteImportSlot(import.slot, reinterpret_cast<uintptr_t>(import.detour))) {
                Log::Loader(std::string("Dunia.log: could not make the ") + import.name +
                            " import slot writable - its output is not captured");
                *import.original = nullptr;
                continue;
            }
            captured += captured.empty() ? "" : ", ";
            captured += import.name;
        }
        Log::Loader("Dunia.log: captured imports: " + (captured.empty() ? "none" : captured));
    }

    void InstallHook(const char* what, uint32_t referenceRva, void* detour, void** original) {
        void* target = reinterpret_cast<void*>(AddressLibrary::Address(referenceRva));
        if (target == nullptr) {
            Log::Loader(std::string("Dunia.log: ") + what +
                        " is not mapped on this build - not captured");
            return;
        }
        if (HookManager::Hook(target, detour, original)) {
            Log::Loader(std::string("Dunia.log: ") + what + " is captured");
        }
    }

} // namespace

void DuniaLog::Install() {
    InstallHook("the console's AddLine", Symbols::kConsoleAddLine,
                reinterpret_cast<void*>(&AddLineDetour), reinterpret_cast<void**>(&g_addLine));
    InstallHook("CScriptSystem::OnScriptError", Symbols::kOnScriptError,
                reinterpret_cast<void*>(&OnScriptErrorDetour),
                reinterpret_cast<void**>(&g_onScriptError));

    g_getParamCount = AddressLibrary::Function<decltype(g_getParamCount)>(
        Symbols::kFunctionHandlerGetParamCount);
    g_luaToString = AddressLibrary::Function<decltype(g_luaToString)>(Symbols::kLuaToString);
    if (g_getParamCount == nullptr || g_luaToString == nullptr) {
        Log::Loader("Dunia.log: the script argument readers are not mapped on this build - "
                    "System:Log is not captured");
    } else {
        InstallHook("System:Log", Symbols::kSystemLog, reinterpret_cast<void*>(&SystemLogDetour),
                    reinterpret_cast<void**>(&g_systemLog));
        InstallHook("System:LogToConsole", Symbols::kSystemLogToConsole,
                    reinterpret_cast<void*>(&SystemLogToConsoleDetour),
                    reinterpret_cast<void**>(&g_systemLogToConsole));
    }

    InstallImports();
}

void DuniaLog::Shutdown() {
    for (Stream& stream : g_streams) {
        if (!stream.pending.empty()) {
            Log::Engine(stream.tag, stream.pending);
            stream.pending.clear();
        }
    }
    for (Import& import : g_imports) {
        if (*import.original != nullptr) {
            WriteImportSlot(import.slot, reinterpret_cast<uintptr_t>(*import.original));
            *import.original = nullptr;
        }
    }
}

} // namespace FCSE
