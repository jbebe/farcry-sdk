#include "lua_api.h"

#include "../caller_identity.h"
#include "../dunia_api.h"
#include "../function_registry.h"
#include "../hook.h"
#include "../log.h"
#include "../patch.h"
#include "../settings_registry.h"

extern "C" {
#include "lauxlib.h"
#include "lua.h"
}

#include <cstdint>
#include <string>
#include <vector>
#include <windows.h>

namespace FCSE {

namespace {
    std::string g_currentScript;

    // Registry key under which the Lua-side setting callbacks are kept. A setting's callback has to
    // outlive the call that registered it - SettingsRegistry invokes it again on every toggle,
    // possibly hours later - so it is anchored in the Lua registry rather than left on the stack.
    constexpr char kSettingCallbacks[] = "fcse.setting_callbacks";

    // The interpreter, captured at Install(). Needed because setting callbacks arrive from
    // SettingsRegistry as plain C function pointers with no lua_State to hand back.
    lua_State* g_L = nullptr;

    // Addresses arrive as Lua numbers. lua_Number is a double, which represents every 32-bit value
    // exactly, so this is lossless for this process - but reject anything that is not a whole,
    // in-range number rather than truncating silently into a wild pointer.
    uintptr_t CheckAddress(lua_State* L, int index, const char* what) {
        lua_Number raw = luaL_checknumber(L, index);
        if (raw < 0 || raw > 4294967295.0) {
            luaL_error(L, "%s: address %f is outside the 32-bit range", what, raw);
        }
        auto address = static_cast<uintptr_t>(raw);
        if (static_cast<lua_Number>(address) != raw) {
            luaL_error(L, "%s: address %f is not a whole number", what, raw);
        }
        return address;
    }

    // ---- fcse.log -------------------------------------------------------------------------------

    int Api_Log(lua_State* L) {
        const char* message = luaL_checkstring(L, 1);
        // Tag the line with the script rather than with FCSE, which is what address-based
        // resolution would report for this shim.
        ScopedCallerIdentity identity(g_currentScript);
        Log::FromCaller(_ReturnAddress(), message);
        return 0;
    }

    // ---- addresses ------------------------------------------------------------------------------

    int Api_DuniaBase(lua_State* L) {
        lua_pushnumber(L, static_cast<lua_Number>(DuniaApi::Base()));
        return 1;
    }

    int Api_DuniaSize(lua_State* L) {
        lua_pushnumber(L, static_cast<lua_Number>(DuniaApi::Size()));
        return 1;
    }

    int Api_CurrentScript(lua_State* L) {
        lua_pushlstring(L, g_currentScript.data(), g_currentScript.size());
        return 1;
    }

    // ---- fcse.mem.write_* / write_bytes ---------------------------------------------------------

    int Api_Patch(lua_State* L) {
        uintptr_t address = CheckAddress(L, 1, "patch");
        size_t size = 0;
        const char* data = luaL_checklstring(L, 2, &size);
        if (size == 0) {
            lua_pushboolean(L, 0);
            return 1;
        }

        // Attribute the patch to the script, so PatchManager's overlap check distinguishes two
        // scripts writing the same bytes (rejected) from one script writing them twice (allowed).
        ScopedCallerIdentity identity(g_currentScript);
        bool ok = PatchManager::Patch(reinterpret_cast<void*>(address), data, size);
        lua_pushboolean(L, ok ? 1 : 0);
        return 1;
    }

    // ---- fcse.hook ------------------------------------------------------------------------------

    int Api_Hook(lua_State* L) {
        uintptr_t target = CheckAddress(L, 1, "hook target");
        uintptr_t detour = CheckAddress(L, 2, "hook detour");

        void* original = nullptr;
        bool ok;
        {
            ScopedCallerIdentity identity(g_currentScript);
            ok = HookManager::Hook(reinterpret_cast<void*>(target),
                                    reinterpret_cast<void*>(detour), &original);
        }

        // nil rather than false on failure: the Lua side treats the result as the trampoline, and a
        // nil is what makes `if trampoline == nil` read naturally there.
        if (!ok || original == nullptr) {
            lua_pushnil(L);
            return 1;
        }
        lua_pushnumber(L, static_cast<lua_Number>(reinterpret_cast<uintptr_t>(original)));
        return 1;
    }

    // ---- fcse.command ---------------------------------------------------------------------------

    int Api_AddFunctionCB(lua_State* L) {
        uintptr_t fn = CheckAddress(L, 1, "command handler");
        const char* name = luaL_checkstring(L, 2);

        ScopedCallerIdentity identity(g_currentScript);
        bool ok = FunctionRegistry::Register(reinterpret_cast<void*>(fn), name);
        lua_pushboolean(L, ok ? 1 : 0);
        return 1;
    }

    // ---- fcse.setting ---------------------------------------------------------------------------

    // Invoked by SettingsRegistry, both during registration and after every in-game toggle.
    // `userdata` is the luaL_ref handle for the script's callback.
    void __cdecl OnSettingChanged(const FCSE_SettingValue* value, void* userdata) {
        if (g_L == nullptr || value == nullptr) {
            return;
        }
        auto ref = static_cast<int>(reinterpret_cast<intptr_t>(userdata));

        lua_State* L = g_L;
        int top = lua_gettop(L);

        lua_getfield(L, LUA_REGISTRYINDEX, kSettingCallbacks);
        lua_rawgeti(L, -1, ref);
        if (!lua_isfunction(L, -1)) {
            lua_settop(L, top);
            return;
        }
        lua_pushboolean(L, value->asCheckbox ? 1 : 0);

        // pcall, not call: this runs from inside the menu's click handler on the game thread, so an
        // error escaping here would unwind through engine code.
        if (lua_pcall(L, 1, 0, 0) != 0) {
            const char* err = lua_tostring(L, -1);
            Log::Loader(std::string("Lua: setting callback failed: ") +
                        (err != nullptr ? err : "unknown error"));
        }
        lua_settop(L, top);
    }

    int Api_RegisterSetting(lua_State* L) {
        const char* name = luaL_checkstring(L, 1);
        luaL_checktype(L, 2, LUA_TBOOLEAN);
        bool defaultValue = lua_toboolean(L, 2) != 0;

        int ref = LUA_NOREF;
        if (!lua_isnoneornil(L, 3)) {
            luaL_checktype(L, 3, LUA_TFUNCTION);
            lua_getfield(L, LUA_REGISTRYINDEX, kSettingCallbacks);
            lua_pushvalue(L, 3);
            ref = luaL_ref(L, -2);
            lua_pop(L, 1);
        }

        FCSE_Setting setting{};
        setting.name = name;
        setting.defaultValue.type = FCSE_SettingType_Checkbox;
        setting.defaultValue.asCheckbox = defaultValue;
        setting.onChanged = ref != LUA_NOREF ? &OnSettingChanged : nullptr;
        setting.userdata = reinterpret_cast<void*>(static_cast<intptr_t>(ref));

        // The group is the script's name, so each script gets its own [group] in fcse.ini and its
        // own rows in the Mod Configuration Menu without doing anything to earn it.
        ScopedCallerIdentity identity(g_currentScript);
        bool ok = SettingsRegistry::RegisterSettings(g_currentScript.c_str(), &setting, 1);
        lua_pushboolean(L, ok ? 1 : 0);
        return 1;
    }

    // ---- fcse.mem.scan --------------------------------------------------------------------------

    // One byte of a signature: a value, or a wildcard that matches anything.
    struct PatternByte {
        uint8_t value;
        bool wildcard;
    };

    // Parses "8B 44 24 ?? 85 C0" into bytes. Accepts `??` or `?` for a wildcard and tolerates any
    // spacing. Returns false on anything it cannot read, rather than guessing - a misparsed
    // signature would silently scan for the wrong thing.
    bool ParsePattern(const char* text, std::vector<PatternByte>& out, std::string& error) {
        out.clear();
        for (const char* p = text; *p != '\0';) {
            if (*p == ' ' || *p == '\t') {
                ++p;
                continue;
            }
            if (*p == '?') {
                ++p;
                if (*p == '?') {
                    ++p;
                }
                out.push_back({0, true});
                continue;
            }

            auto nibble = [](char c, int& value) {
                if (c >= '0' && c <= '9') { value = c - '0'; return true; }
                if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
                if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
                return false;
            };

            int high = 0;
            int low = 0;
            if (!nibble(p[0], high) || p[1] == '\0' || !nibble(p[1], low)) {
                error = std::string("expected two hex digits or '??' at \"") + p + "\"";
                return false;
            }
            out.push_back({static_cast<uint8_t>((high << 4) | low), false});
            p += 2;
        }

        if (out.empty()) {
            error = "pattern is empty";
            return false;
        }
        return true;
    }

    // Dunia's executable section, which is what a code signature is looking for. Scanning the whole
    // module would also walk .data and .rsrc - slower, and a "hit" there is not an instruction.
    bool DuniaTextRange(uintptr_t& begin, size_t& size) {
        uintptr_t base = DuniaApi::Base();
        if (base == 0) {
            return false;
        }
        auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
            return false;
        }
        auto nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) {
            return false;
        }
        begin = base + nt->OptionalHeader.BaseOfCode;
        size = nt->OptionalHeader.SizeOfCode;
        return true;
    }

    // Collects matches, stopping after `limit` (0 = no limit).
    void ScanText(const std::vector<PatternByte>& pattern, size_t limit,
                  std::vector<uintptr_t>& hits) {
        uintptr_t begin = 0;
        size_t size = 0;
        if (!DuniaTextRange(begin, size) || pattern.size() > size) {
            return;
        }

        const auto* bytes = reinterpret_cast<const uint8_t*>(begin);
        const size_t last = size - pattern.size();
        const size_t count = pattern.size();

        for (size_t i = 0; i <= last; ++i) {
            size_t j = 0;
            for (; j < count; ++j) {
                if (!pattern[j].wildcard && bytes[i + j] != pattern[j].value) {
                    break;
                }
            }
            if (j == count) {
                hits.push_back(begin + i);
                if (limit != 0 && hits.size() >= limit) {
                    return;
                }
            }
        }
    }

    int Api_Scan(lua_State* L) {
        const char* text = luaL_checkstring(L, 1);
        std::vector<PatternByte> pattern;
        std::string error;
        if (!ParsePattern(text, pattern, error)) {
            return luaL_error(L, "scan: %s", error.c_str());
        }

        std::vector<uintptr_t> hits;
        ScanText(pattern, 1, hits);
        lua_pushnumber(L, hits.empty() ? 0 : static_cast<lua_Number>(hits.front()));
        return 1;
    }

    int Api_ScanAll(lua_State* L) {
        const char* text = luaL_checkstring(L, 1);
        std::vector<PatternByte> pattern;
        std::string error;
        if (!ParsePattern(text, pattern, error)) {
            return luaL_error(L, "scan_all: %s", error.c_str());
        }

        std::vector<uintptr_t> hits;
        ScanText(pattern, 0, hits);

        lua_createtable(L, static_cast<int>(hits.size()), 0);
        for (size_t i = 0; i < hits.size(); ++i) {
            lua_pushnumber(L, static_cast<lua_Number>(hits[i]));
            lua_rawseti(L, -2, static_cast<int>(i + 1));
        }
        return 1;
    }

    const luaL_Reg kApi[] = {
        {"log", &Api_Log},
        {"dunia_base", &Api_DuniaBase},
        {"dunia_size", &Api_DuniaSize},
        {"current_script", &Api_CurrentScript},
        {"patch", &Api_Patch},
        {"hook", &Api_Hook},
        {"add_function_cb", &Api_AddFunctionCB},
        {"register_setting", &Api_RegisterSetting},
        {"scan", &Api_Scan},
        {"scan_all", &Api_ScanAll},
        {nullptr, nullptr},
    };
}

void LuaApi::Install(lua_State* L) {
    g_L = L;

    // The anchor table for setting callbacks. Created before anything can register one.
    lua_newtable(L);
    lua_setfield(L, LUA_REGISTRYINDEX, kSettingCallbacks);

    lua_createtable(L, 0, static_cast<int>(sizeof(kApi) / sizeof(kApi[0]) - 1));
    for (const luaL_Reg* entry = kApi; entry->name != nullptr; ++entry) {
        lua_pushcfunction(L, entry->func);
        lua_setfield(L, -2, entry->name);
    }
    lua_setglobal(L, "_FCSE_NATIVE");
}

void LuaApi::SetCurrentScript(const std::string& name) { g_currentScript = name; }

const std::string& LuaApi::CurrentScript() { return g_currentScript; }

} // namespace FCSE
