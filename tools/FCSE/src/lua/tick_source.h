#pragma once

#include <string>

// Drives LuaHost::Tick() from the engine's own per-frame update.
//
// The hook target is `CXGame::Update` (0x1065aea0) - the body of Dunia's frame loop. It reads the
// frame delta, calls CGame::Update (0x100419e0) with it, dispatches the "incHB" registry callback,
// then runs CCryEngine::Update and CDynamicEnvironmentManager::Update. Hooking it means a script's
// 'update' fires exactly once per frame, in step with the engine's own update order.
//
// An earlier version instead detoured FunctionRegistry_Invoke and ticked when the "incHB" name was
// dispatched. That was a proxy for a frame boundary rather than a frame boundary, and it inherited
// two failure modes for free: it depended on the CRC32 of a name matching the engine's (it did not,
// at first), and on that name being dispatched at all. Hooking the frame function directly has
// neither problem - there is no name, no hash, and nothing to guess. The registry dispatch of
// "incHB" visible inside CXGame::Update is what made the old approach look plausible; it is a
// callback the frame loop happens to make, not the frame itself.
namespace FCSE {

class TickSource {
public:
    // Installs the frame hook. Call after HookManager::Initialize, after Dunia is resolved, and
    // after LuaHost::Init so there is an interpreter to tick. Returns false (logged) on failure.
    static bool Install();

    // Logs the measured frame rate once, after this many ticks, as a self-check that the hook is
    // live and firing at a plausible rate. Zero disables it. Settable from fcse.ini.
    static void SetSelfCheckTicks(int ticks);

    // Reports whether the hook ever fired, at shutdown. "No update events" is otherwise silent.
    static void Finish();
};

} // namespace FCSE
