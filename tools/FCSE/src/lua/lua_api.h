#pragma once

struct lua_State;

#include <string>

// The native half of the script API: the handful of things LuaJIT's FFI genuinely cannot do for
// itself, which is FCSE's own hook/patch/settings/function-registry bookkeeping.
//
// Everything a script actually calls lives in runtime/fcse.lua and is built out of these. Keeping
// the native surface this thin is deliberate - it is the part that cannot be changed without a
// rebuild, and the part where a mistake is a crash rather than a Lua error.
//
// Addresses cross the boundary as Lua numbers, never as cdata: cdata is a LuaJIT value type the
// standard Lua C API cannot inspect, and Far Cry 2 being 32-bit means a double carries any address
// exactly.
namespace FCSE {

class LuaApi {
public:
    // Installs the native table as the global _FCSE_NATIVE. runtime/fcse.lua consumes it and clears
    // the global, so scripts only ever see the wrapped API.
    static void Install(lua_State* L);

    // Names the script currently running, for log tags, hook/patch ownership and setting groups.
    // The host sets this around every call into a script; it is what C.current_script() returns.
    static void SetCurrentScript(const char* name);
    static const std::string& CurrentScript();
};

} // namespace FCSE
