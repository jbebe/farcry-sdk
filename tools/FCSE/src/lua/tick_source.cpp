#include "lua/tick_source.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/dunia_api.h"
#include "api/hook.h"
#include "log.h"
#include "lua/lua_host.h"

#include <cstdint>
#include <cstdio>
#include <string>
#include <windows.h>

namespace FCSE {

namespace {
    // CXGame::Update is Symbols::kUpdate, the frame loop body - see the header for how it was
    // identified. Symbols::kTimeBlock is the global holding the engine's timing block; the frame
    // delta is the double at +0x38. CXGame::Update reads it as
    // `(float)*(double *)(DAT_11606360 + 0x38)` and passes it to CGame::Update as that call's time
    // argument, which is what identifies it as the frame delta rather than an absolute clock.
    //
    // Both come from the address library rather than a baked RVA: the two shipped v1.03 builds put
    // them at different addresses, and 0x1065AEA0 is only correct on Steam/Uplay.
    constexpr ptrdiff_t kFrameDeltaOffset = 0x38;

    // One int parameter in ecx, which is what __fastcall expresses in a free function - the unused
    // edx slot is the second parameter and is never read.
    using UpdateFn = void(__fastcall*)(void*, void*);
    UpdateFn g_original = nullptr;

    uintptr_t g_timeBlock = 0;
    bool g_inTick = false;

    uint64_t g_ticks = 0;
    int g_selfCheckTicks = 300;
    bool g_selfCheckDone = false;
    DWORD g_firstTick = 0;

    // Reads the frame delta, or 0 if the timing block is not up yet. Returning 0 rather than
    // guessing keeps a script's own accumulator honest on the first frames.
    double FrameDelta() {
        if (g_timeBlock == 0) {
            return 0.0;
        }
        auto block = *reinterpret_cast<uintptr_t*>(g_timeBlock);
        if (block == 0) {
            return 0.0;
        }
        return *reinterpret_cast<double*>(block + kFrameDeltaOffset);
    }

    void __fastcall UpdateDetour(void* self, void* edx) {
        // Engine first, so a script observes the frame the engine has already finished updating
        // rather than a half-applied one, and so a slow script cannot reorder engine work.
        g_original(self, edx);

        if (g_inTick) {
            return; // a script's handler caused a re-entrant frame; do not recurse
        }

        ++g_ticks;
        if (g_firstTick == 0) {
            g_firstTick = GetTickCount();
        }

        g_inTick = true;
        LuaHost::Tick(FrameDelta());
        g_inTick = false;

        // Fires once. "No update events" was previously indistinguishable from "the hook is on the
        // wrong function", and a rate that is plausible is the cheapest proof that it is neither.
        if (!g_selfCheckDone && g_selfCheckTicks > 0 &&
            g_ticks >= static_cast<uint64_t>(g_selfCheckTicks)) {
            g_selfCheckDone = true;
            DWORD elapsed = GetTickCount() - g_firstTick;
            char line[192];
            std::snprintf(line, sizeof(line),
                           "Tick: %llu frames in %lu ms (%.1f/s), frame delta %.4f s",
                           static_cast<unsigned long long>(g_ticks), elapsed,
                           elapsed > 0 ? (g_ticks * 1000.0 / elapsed) : 0.0, FrameDelta());
            Log::Loader(line);
        }
    }
}

void TickSource::SetSelfCheckTicks(int ticks) { g_selfCheckTicks = ticks; }

bool TickSource::Install() {
    uintptr_t base = DuniaApi::Base();
    if (base == 0) {
        Log::Loader("Tick: Dunia.dll is not resolved - no script 'update' events this run");
        return false;
    }

    // Resolve everything before touching anything. A half-installed tick source would hook the
    // frame loop and then read a frame delta from address 0 on every frame.
    const uintptr_t update = AddressLibrary::Address(Symbols::kUpdate);
    const uintptr_t timeBlock = AddressLibrary::Address(Symbols::kTimeBlock);
    if (update == 0 || timeBlock == 0) {
        Log::Loader("Tick: CXGame::Update or the engine timing block has no address on this game "
                    "build - no script 'update' events this run");
        return false;
    }

    g_timeBlock = timeBlock;

    void* target = reinterpret_cast<void*>(update);
    if (!HookManager::Hook(target, reinterpret_cast<void*>(&UpdateDetour),
                            reinterpret_cast<void**>(&g_original))) {
        Log::Loader("Tick: could not hook CXGame::Update - no script 'update' events this run");
        return false;
    }

    char line[128];
    std::snprintf(line, sizeof(line), "Tick: hooked CXGame::Update at 0x%08zX",
                   static_cast<size_t>(update));
    Log::Loader(line);
    return true;
}

void TickSource::Finish() {
    if (g_ticks == 0) {
        Log::Loader("Tick: CXGame::Update never fired this session - no script 'update' events ran. "
                    "If the game did reach gameplay, the address library resolved CXGame::Update to "
                    "the wrong function on this build - report the build line above.");
        return;
    }
    Log::Loader("Tick: " + std::to_string(g_ticks) + " frame(s) dispatched this session");
}

} // namespace FCSE
