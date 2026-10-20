// The engine's Lua 4 state: run a chunk in it, and give it C functions of our own.
#include "engine/lua.h"

#include "fcse_api.h"

namespace {
    using GetScriptSystemFn = void*(__cdecl*)();
    using ExecuteBufferFn = bool(__thiscall*)(void* scripts, const char* code, size_t length,
                                              const char* chunk);
    using PushCClosureFn = void(__cdecl*)(void* state, WildlifeOverhaul::Lua::CFunction function,
                                          int upvalues);
    using SetGlobalFn = void(__cdecl*)(void* state, const char* name);

    FCSE::Relocation<GetScriptSystemFn> g_getScriptSystem{FCSE::Uplay(0x002A9760)};
    FCSE::Relocation<ExecuteBufferFn> g_executeBuffer{FCSE::Uplay(0x002A9FF0)};
    FCSE::Relocation<PushCClosureFn> g_pushCClosure{FCSE::Uplay(0x002C4D90)};
    FCSE::Relocation<SetGlobalFn> g_setGlobal{FCSE::Uplay(0x002AB270)};

    constexpr char kChunk[] = "wildlife-overhaul";
}

namespace WildlifeOverhaul::Lua {

bool Found() { return g_getScriptSystem && g_executeBuffer && g_pushCClosure && g_setGlobal; }

bool Run(const std::string& code, const char* name, CFunction function) {
    void* scripts = g_getScriptSystem.get()();
    if (scripts == nullptr) {
        return false;
    }
    // The script system's first member is its lua_State.
    void* state = *static_cast<void**>(scripts);
    g_pushCClosure.get()(state, function, 0);
    g_setGlobal.get()(state, name);
    return g_executeBuffer.get()(scripts, code.c_str(), code.size(), kChunk);
}

}
