#include "lua_host.h"

#include "../log.h"
#include "lua_api.h"

extern "C" {
#include "lauxlib.h"
#include "lua.h"
#include "lualib.h"
}

#include <string>
#include <vector>
#include <windows.h>

namespace FCSE {

namespace {
    lua_State* g_state = nullptr;
    int g_loadedScripts = 0;

    // Resource name, not a path - runtime/fcse.lua is embedded by CMake from assets/fcse.rc.in, same
    // as the .mgb layouts. Unquoted non-numeric names in a .rc are string names, so there is no
    // resource.h to keep in sync.
    constexpr wchar_t kRuntimeResource[] = L"FCSE_LUA_RUNTIME";

    // An 'update' handler that throws every frame would otherwise write a traceback per frame until
    // the log filled the disk. After this many consecutive failures the handler is dropped for the
    // rest of the session and says so once.
    constexpr int kMaxConsecutiveFailures = 3;

    std::string Narrow(const std::wstring& wide) {
        if (wide.empty()) {
            return "";
        }
        int len = WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()),
                                       nullptr, 0, nullptr, nullptr);
        if (len <= 0) {
            return "";
        }
        std::string result(len, '\0');
        WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()), result.data(),
                             len, nullptr, nullptr);
        return result;
    }

    // Reads a script into memory. Scripts are small; nothing here needs streaming. Named for what it
    // reads rather than the generic ReadFile, which is a Win32 function this calls - the shadowing
    // would otherwise turn the call below into unbounded recursion.
    bool ReadScriptFile(const std::wstring& path, std::string& out) {
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                                   OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            return false;
        }
        LARGE_INTEGER size{};
        if (!GetFileSizeEx(file, &size) || size.QuadPart > (16 << 20)) {
            CloseHandle(file);
            return false;
        }
        out.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        bool ok = out.empty() ||
                  (::ReadFile(file, out.data(), static_cast<DWORD>(out.size()), &read, nullptr) &&
                   read == out.size());
        CloseHandle(file);
        return ok;
    }

    // The runtime lives in FCSE.exe's own image, not in Dunia.dll - hence GetModuleHandleW(nullptr).
    // Nothing needs freeing: LockResource returns a pointer into the mapped image.
    bool FindEmbeddedRuntime(const char** data, size_t* size) {
        HMODULE self = GetModuleHandleW(nullptr);
        // MAKEINTRESOURCEW(10) rather than RT_RCDATA: this target does not define UNICODE, so
        // RT_RCDATA expands to the ANSI form and will not pass to FindResourceW.
        HRSRC found = FindResourceW(self, kRuntimeResource, MAKEINTRESOURCEW(10));
        if (found == nullptr) {
            return false;
        }
        HGLOBAL block = LoadResource(self, found);
        if (block == nullptr) {
            return false;
        }
        const void* bytes = LockResource(block);
        DWORD length = SizeofResource(self, found);
        if (bytes == nullptr || length == 0) {
            return false;
        }
        *data = static_cast<const char*>(bytes);
        *size = length;
        return true;
    }

    // Pushes debug.traceback to be used as a protected call's message handler, so a failure reports
    // where in the script it happened rather than just what went wrong.
    int PushTracebackHandler(lua_State* L) {
        lua_getglobal(L, "debug");
        lua_getfield(L, -1, "traceback");
        lua_remove(L, -2);
        return lua_gettop(L);
    }

    std::string PopError(lua_State* L) {
        const char* text = lua_tostring(L, -1);
        std::string message = text != nullptr ? text : "unknown error";
        lua_pop(L, 1);
        return message;
    }

    // Loads the embedded runtime and puts the module it returns into package.loaded, so a script's
    // `require 'fcse'` resolves without ever touching the filesystem.
    bool InstallRuntime(lua_State* L) {
        const char* data = nullptr;
        size_t size = 0;
        if (!FindEmbeddedRuntime(&data, &size)) {
            Log::Loader("Lua: the embedded fcse.lua runtime is missing from this build - check that "
                        "enable_language(RC) ran and that the generated fcse.rc reached the link");
            return false;
        }

        int handler = PushTracebackHandler(L);

        // "@fcse.lua" - the '@' marks it as a chunk name so errors read "fcse.lua:12:" rather than
        // quoting the whole source back at the reader.
        if (luaL_loadbuffer(L, data, size, "@fcse.lua") != 0) {
            Log::Loader("Lua: the embedded runtime failed to compile: " + PopError(L));
            lua_remove(L, handler);
            return false;
        }
        if (lua_pcall(L, 0, 1, handler) != 0) {
            Log::Loader("Lua: the embedded runtime failed to run: " + PopError(L));
            lua_remove(L, handler);
            return false;
        }
        lua_remove(L, handler);

        if (!lua_istable(L, -1)) {
            Log::Loader("Lua: the embedded runtime did not return a module table");
            lua_pop(L, 1);
            return false;
        }

        lua_getglobal(L, "package");
        lua_getfield(L, -1, "loaded");
        lua_pushvalue(L, -3);
        lua_setfield(L, -2, "fcse");
        lua_pop(L, 3); // loaded, package, module
        return true;
    }

    // One discovered script: the name it is known by, and the file to run.
    struct ScriptFile {
        std::string name;
        std::wstring path;
    };

    // Finds scripts under `directory` - bin\plugins\, the same folder the plugin DLLs live in -
    // recursively, in two accepted shapes:
    //
    //   plugins\quick_tweak.lua        a single file - the whole point of scripting being cheap
    //   plugins\my_mod\main.lua        a folder per mod - room for extra files alongside
    //
    // Sharing the folder with the DLLs is deliberate: a player installs a mod by dropping it into
    // bin\plugins\ and should not have to know which language it happens to be written in. The two
    // scans cannot fight over anything - PluginLoader matches *.dll and this matches *.lua.
    //
    // A directory containing main.lua is one mod, not a bag of scripts: only its main.lua is run,
    // and the walk does not descend past it. Without that rule a mod shipping helper files would
    // have every one of them executed as a separate script - out of order, and with the libraries
    // running before the file that was supposed to require them. Helpers stay reachable through
    // require, which is what they are for.
    //
    // Any other directory is just a container and is walked through, so mods can be grouped into
    // folders without that changing what runs.
    //
    // The name is the folder or file stem, and is what fcse.ini groups, log tags and the Mod
    // Configuration Menu use - so it is what a player sees.
    void Discover(const std::wstring& directory, std::vector<ScriptFile>& found) {
        WIN32_FIND_DATAW entry;
        HANDLE search = FindFirstFileW((directory + L"*").c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE) {
            return;
        }

        std::vector<std::wstring> subdirectories;
        do {
            std::wstring name = entry.cFileName;
            if (name == L"." || name == L"..") {
                continue;
            }

            if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                std::wstring folder = directory + name + L"\\";
                std::wstring main = folder + L"main.lua";
                if (GetFileAttributesW(main.c_str()) != INVALID_FILE_ATTRIBUTES) {
                    found.push_back({Narrow(name), main}); // a mod: stop here
                } else {
                    subdirectories.push_back(folder); // a container: keep walking
                }
                continue;
            }

            // Case-insensitive ".lua" suffix, since Windows paths are.
            if (name.size() > 4 && _wcsicmp(name.c_str() + name.size() - 4, L".lua") == 0) {
                found.push_back({Narrow(name.substr(0, name.size() - 4)), directory + name});
            }
        } while (FindNextFileW(search, &entry));

        FindClose(search); // before recursing, rather than held open across the whole tree

        for (const std::wstring& subdirectory : subdirectories) {
            Discover(subdirectory, found);
        }
    }

    // Runs one script in an environment of its own.
    //
    // The environment is a fresh table reading through to _G, so a script's globals are private -
    // two scripts can each keep a `config` without colliding - while the standard library stays
    // visible. Writes land in the private table, so a script also cannot clobber _G for everyone
    // else, accidentally or otherwise.
    bool RunScript(lua_State* L, const ScriptFile& script) {
        std::string source;
        if (!ReadScriptFile(script.path, source)) {
            Log::Loader("Lua: could not read " + Narrow(script.path));
            return false;
        }

        LuaApi::SetCurrentScript(script.name);

        int handler = PushTracebackHandler(L);
        std::string chunkName = "@" + script.name + ".lua";
        if (luaL_loadbuffer(L, source.data(), source.size(), chunkName.c_str()) != 0) {
            Log::Loader("Lua: '" + script.name + "' failed to compile: " + PopError(L));
            lua_remove(L, handler);
            return false;
        }

        // The environment table, with _G behind it.
        lua_newtable(L);
        lua_newtable(L);
        lua_getglobal(L, "_G");
        lua_setfield(L, -2, "__index");
        lua_setmetatable(L, -2);
        lua_setfenv(L, -2);

        if (lua_pcall(L, 0, 0, handler) != 0) {
            Log::Loader("Lua: '" + script.name + "' failed: " + PopError(L));
            lua_remove(L, handler);
            return false;
        }
        lua_remove(L, handler);
        return true;
    }

    // Walks fcse._handlers[event] and calls each entry, containing failures per handler.
    //
    // Leaves the handler list in place on success and drops an entry that has failed
    // kMaxConsecutiveFailures times in a row - which only realistically happens for 'update', where
    // a broken handler would otherwise fail once per frame forever.
    void Dispatch(const char* event, const double* argument = nullptr) {
        lua_State* L = g_state;
        if (L == nullptr) {
            return;
        }

        int top = lua_gettop(L);

        lua_getglobal(L, "package");
        lua_getfield(L, -1, "loaded");
        lua_getfield(L, -1, "fcse");
        if (!lua_istable(L, -1)) {
            lua_settop(L, top);
            return;
        }
        lua_getfield(L, -1, "_handlers");
        lua_getfield(L, -1, event);
        if (!lua_istable(L, -1)) {
            lua_settop(L, top);
            return;
        }

        int handlers = lua_gettop(L);
        int errorHandler = PushTracebackHandler(L);

        int count = static_cast<int>(lua_objlen(L, handlers));
        for (int i = 1; i <= count; ++i) {
            lua_rawgeti(L, handlers, i);
            if (!lua_istable(L, -1)) {
                lua_pop(L, 1);
                continue;
            }
            int entry = lua_gettop(L);

            lua_getfield(L, entry, "script");
            std::string owner = lua_isstring(L, -1) ? lua_tostring(L, -1) : "?";
            lua_pop(L, 1);

            lua_getfield(L, entry, "fn");
            if (!lua_isfunction(L, -1)) {
                lua_settop(L, entry - 1);
                continue;
            }

            LuaApi::SetCurrentScript(owner);
            int argCount = 0;
            if (argument != nullptr) {
                lua_pushnumber(L, static_cast<lua_Number>(*argument));
                argCount = 1;
            }
            if (lua_pcall(L, argCount, 0, errorHandler) != 0) {
                std::string message = PopError(L);

                lua_getfield(L, entry, "failures");
                int failures = static_cast<int>(lua_tointeger(L, -1)) + 1;
                lua_pop(L, 1);
                lua_pushinteger(L, failures);
                lua_setfield(L, entry, "failures");

                if (failures >= kMaxConsecutiveFailures) {
                    // Replacing fn with nil leaves the entry in place, so the indices of the
                    // handlers after it do not shift while this loop is walking them.
                    lua_pushnil(L);
                    lua_setfield(L, entry, "fn");
                    Log::Loader("Lua: '" + owner + "' " + event + " handler failed " +
                                std::to_string(failures) + " times in a row and was disabled: " +
                                message);
                } else {
                    Log::Loader("Lua: '" + owner + "' " + event + " handler failed: " + message);
                }
            } else {
                lua_pushinteger(L, 0);
                lua_setfield(L, entry, "failures");
            }

            lua_settop(L, entry - 1);
        }

        lua_settop(L, top);
    }

    // Reports the interpreter's own view of itself rather than anything baked in at compile time, so
    // the log names the runtime that actually loaded. JIT and FFI are both called out because both
    // are load-bearing: FFI is how a script reaches engine memory at all, and a silently
    // interpreter-only build would be a performance cliff for anything running per frame.
    void LogInterpreterBanner(lua_State* L) {
        static const char* kProbe =
            "local ffi = require('ffi')\n"
            "return jit.version .. ' | ' .. jit.arch .. '/' .. jit.os\n"
            "    .. ' | jit=' .. tostring(jit.status())\n"
            "    .. ' | ffi=' .. tostring(ffi ~= nil)\n";

        if (luaL_loadstring(L, kProbe) != 0 || lua_pcall(L, 0, 1, 0) != 0) {
            Log::Loader("Lua: interpreter self-check failed: " + PopError(L));
            return;
        }
        const char* report = lua_tostring(L, -1);
        Log::Loader(std::string("Lua: ") + (report != nullptr ? report : "?"));
        lua_pop(L, 1);
    }
}

bool LuaHost::Init(const std::wstring& pluginsDirectory) {
    if (g_state != nullptr) {
        return true;
    }

    g_state = luaL_newstate();
    if (g_state == nullptr) {
        Log::Loader("Lua: luaL_newstate failed - no script runtime this run");
        return false;
    }

    lua_State* L = g_state;
    luaL_openlibs(L);
    LogInterpreterBanner(L);

    LuaApi::Install(L);
    if (!InstallRuntime(L)) {
        lua_close(L);
        g_state = nullptr;
        return false;
    }

    // PluginLoader has already created it by this point; harmless either way.
    CreateDirectoryW(pluginsDirectory.c_str(), nullptr);

    std::vector<ScriptFile> scripts;
    Discover(pluginsDirectory, scripts);
    if (scripts.empty()) {
        Log::Loader("Lua: no scripts found in " + Narrow(pluginsDirectory));
        return true;
    }

    for (const ScriptFile& script : scripts) {
        if (RunScript(L, script)) {
            ++g_loadedScripts;
            Log::Loader("Lua: script '" + script.name + "' loaded");
        }
    }
    LuaApi::SetCurrentScript("");

    Log::Loader("Lua: " + std::to_string(g_loadedScripts) + " of " +
                std::to_string(scripts.size()) + " script(s) loaded");

    Dispatch("load");
    LuaApi::SetCurrentScript("");
    return true;
}

void LuaHost::OnRegisterFunctions() {
    Dispatch("register_functions");
    LuaApi::SetCurrentScript("");
}

void LuaHost::Tick(double deltaSeconds) { Dispatch("update", &deltaSeconds); }

void LuaHost::Shutdown() {
    if (g_state == nullptr) {
        return;
    }
    lua_close(g_state);
    g_state = nullptr;
    Log::Loader("Lua: interpreter closed");
}

bool LuaHost::IsRunning() { return g_state != nullptr; }

int LuaHost::LoadedScriptCount() { return g_loadedScripts; }

} // namespace FCSE
