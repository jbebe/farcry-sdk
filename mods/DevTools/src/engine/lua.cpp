// The engine's Lua: run a chunk, read a value back, and keep the lines the retail build drops.
//
// Every site is a function start, so all of them come from the address library. Lua 4 keeps a
// state's stack top as the state's first member, which is what Evaluate restores in place of a
// lua_settop the build does not name.
#include "engine/lua.h"

#include "engine/game_thread.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iterator>
#include <deque>
#include <mutex>

namespace {
    using GetScriptSystemFn = void*(__cdecl*)();
    using ExecuteBufferFn = bool(__thiscall*)(void* scripts, const char* code, size_t length,
                                              const char* chunk);
    using GetGlobalFn = void(__cdecl*)(void* state, const char* name);
    using ToStringFn = const char*(__cdecl*)(void* state, int index);
    using OnScriptErrorFn = void(__fastcall*)(void* scripts, void* edx, const char* source,
                                              const char* function, int line, const char* message);
    // System:Log: __thiscall(scriptObject, CFunctionHandler*), whose retail body only checks the
    // argument count.
    using SystemLogFn = int(__fastcall*)(void* system, void* edx, void* handler);
    using GetParamCountFn = int(__fastcall*)(void* handler);

    FCSE::Relocation<GetScriptSystemFn> g_getScriptSystemSite{FCSE::Uplay(0x002A9760)};
    FCSE::Relocation<ExecuteBufferFn> g_executeBufferSite{FCSE::Uplay(0x002A9FF0)};
    FCSE::Relocation<GetGlobalFn> g_getGlobalSite{FCSE::Uplay(0x002AAF90)};
    FCSE::Relocation<ToStringFn> g_toStringSite{FCSE::Uplay(0x002AAD40)};
    FCSE::Relocation<OnScriptErrorFn> g_onScriptErrorSite{FCSE::Uplay(0x002A9AE0)};
    FCSE::Relocation<SystemLogFn> g_systemLogSite{FCSE::Uplay(0x005F8C70)};
    FCSE::Relocation<GetParamCountFn> g_getParamCountSite{FCSE::Uplay(0x002BE150)};

    // CDominoInputListener::Execute: __thiscall(listener, const CActionValue*, SContext*). It hands
    // InputDominoMove, the one signal Domino scripts can hear, to the player as a script event.
    using DominoInputFn = void(__fastcall*)(void* listener, void* edx, const uint32_t* value,
                                            void* context);
    FCSE::Relocation<DominoInputFn> g_dominoInputSite{FCSE::Pattern(
        "8B 4C 24 08 8B 09 83 EC 34 56 8D 44 24 04 50 E8 ?? ?? ?? ?? 8B 44 24 04 83 78 0C 00 0F 84 "
        "?? ?? ?? ?? 8B 0D ?? ?? ?? ?? F6 C1 01 75 ?? 83 C9 01 89 0D ?? ?? ?? ?? C7 05 ?? ?? ?? ?? "
        "52 82 65 62")};
    DominoInputFn g_originalDominoInput = nullptr;

    constexpr uint32_t kInputDominoMove = 0x62658252;

    GetScriptSystemFn g_getScriptSystem = nullptr;
    ExecuteBufferFn g_executeBuffer = nullptr;
    GetGlobalFn g_getGlobal = nullptr;
    ToStringFn g_toString = nullptr;
    GetParamCountFn g_getParamCount = nullptr;
    OnScriptErrorFn g_originalOnScriptError = nullptr;
    SystemLogFn g_originalSystemLog = nullptr;

    // The handler's lua_State sits at +4, and argument N at stack index N + 1.
    constexpr ptrdiff_t kHandlerState = 4;

    constexpr char kChunk[] = "devtools";
    constexpr char kResultGlobal[] = "__devtools_result";
    constexpr size_t kMaxLines = 200;

    std::mutex g_lock;
    std::deque<std::string> g_lines;

    // Keeps each line of `text` under `tag`, in fcse.log too so it survives a crash that follows.
    void AddLines(const char* tag, const char* text) {
        std::lock_guard<std::mutex> held(g_lock);
        const char* start = text;
        while (true) {
            const char* end = std::strchr(start, '\n');
            std::string line(start, end != nullptr ? end - start : std::strlen(start));
            if (!line.empty()) {
                FCSE::Logf("lua %s: %s", tag, line.c_str());
                g_lines.push_back(std::string(tag) + ": " + line);
                if (g_lines.size() > kMaxLines) {
                    g_lines.pop_front();
                }
            }
            if (end == nullptr) {
                return;
            }
            start = end + 1;
        }
    }

    void __fastcall OnScriptErrorDetour(void* scripts, void* edx, const char* source,
                                        const char* function, int line, const char* message) {
        std::string text = message != nullptr ? message : "unknown error";
        if (function != nullptr) {
            text += " - in function ";
            text += function;
        }
        if (source != nullptr) {
            text += " (" + std::string(source) + ":" + std::to_string(line) + ")";
        }
        AddLines("error", text.c_str());
        g_originalOnScriptError(scripts, edx, source, function, line, message);
    }

    int __fastcall SystemLogDetour(void* system, void* edx, void* handler) {
        if (g_getParamCount(handler) == 1) {
            void* state = *reinterpret_cast<void**>(static_cast<uint8_t*>(handler) + kHandlerState);
            if (const char* text = g_toString(state, 2)) {
                AddLines("log", text);
            }
        }
        return g_originalSystemLog(system, edx, handler);
    }

    // Every signal the Domino listener is offered, once each, and InputDominoMove every time.
    void __fastcall DominoInputDetour(void* listener, void* edx, const uint32_t* value,
                                      void* context) {
        static uint32_t seen[64];
        static size_t seenCount = 0;

        const uint32_t signal = *value;
        if (signal == kInputDominoMove) {
            AddLines("input", "InputDominoMove reached the Domino listener");
        } else if (seenCount < std::size(seen) &&
                   std::find(seen, seen + seenCount, signal) == seen + seenCount) {
            seen[seenCount++] = signal;
            char text[48];
            std::snprintf(text, sizeof(text), "signal %08X", signal);
            AddLines("input", text);
        }
        g_originalDominoInput(listener, edx, value, context);
    }

    // Runs on the game thread. False when the chunk did not run, or raised an error.
    bool Run(const std::string& code) {
        void* scripts = g_executeBuffer != nullptr ? g_getScriptSystem() : nullptr;
        if (scripts == nullptr) {
            AddLines("devtools", "no script system yet");
            return false;
        }
        return g_executeBuffer(scripts, code.c_str(), code.size(), kChunk);
    }

    template <typename Fn>
    void HookSite(FCSE::Relocation<Fn>& site, Fn detour, Fn& original, const char* name) {
        if (!site) {
            FCSE::Logf("lua: %s was not found in this build - its lines stay dropped", name);
            return;
        }
        if (FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                     reinterpret_cast<void*>(detour),
                                     reinterpret_cast<void**>(&original))) {
            FCSE::Logf("lua: hooked %s at 0x%08zX", name, static_cast<size_t>(site.address()));
        }
    }
}

namespace DevTools::Lua {

void Install() {
    if (!g_getScriptSystemSite || !g_executeBufferSite || !g_getGlobalSite || !g_toStringSite ||
        !g_getParamCountSite) {
        FCSE::ApiPointer()->Log("lua: the script system was not found in this build - the Lua tab "
                                "is disabled");
        return;
    }

    g_getScriptSystem = g_getScriptSystemSite.get();
    g_executeBuffer = g_executeBufferSite.get();
    g_getGlobal = g_getGlobalSite.get();
    g_toString = g_toStringSite.get();
    g_getParamCount = g_getParamCountSite.get();

    HookSite(g_onScriptErrorSite, &OnScriptErrorDetour, g_originalOnScriptError,
             "CScriptSystem::OnScriptError");
    HookSite(g_systemLogSite, &SystemLogDetour, g_originalSystemLog, "System:Log");
    HookSite(g_dominoInputSite, &DominoInputDetour, g_originalDominoInput,
             "CDominoInputListener::Execute");
}

void Post(const std::string& code) {
    GameThread::Post([code] {
        AddLines(">", code.c_str());
        Run(code);
    });
}

void PostEvaluate(const std::string& expression) {
    GameThread::Post([expression] {
        AddLines("=", expression.c_str());
        if (!Run(std::string(kResultGlobal) + " = tostring(" + expression + ")")) {
            return;
        }

        void* state = *static_cast<void**>(g_getScriptSystem());
        char** top = static_cast<char**>(state);
        char* saved = *top;
        g_getGlobal(state, kResultGlobal);
        const char* value = g_toString(state, -1);
        AddLines("value", value != nullptr ? value : "(not a string)");
        *top = saved;
    });
}

std::vector<std::string> Lines() {
    std::lock_guard<std::mutex> held(g_lock);
    return {g_lines.begin(), g_lines.end()};
}

void Clear() {
    std::lock_guard<std::mutex> held(g_lock);
    g_lines.clear();
}

}
