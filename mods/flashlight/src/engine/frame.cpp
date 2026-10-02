// The end of the engine's frame, as somewhere to run code once a frame on the game thread.
#include "engine/frame.h"

#include "fcse_api.h"

#include <chrono>

namespace {
    // CXGame::Update: __thiscall with no stack arguments, which a free function spells __fastcall.
    using UpdateFn = void(__fastcall*)(void* self, void* unused);

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x0065AEA0)};
    UpdateFn g_originalUpdate = nullptr;

    // Longer steps, a hitch or a load, count as no time at all.
    constexpr float kMaxSeconds = 0.25f;

    Flashlight::Frame::TickFn g_tick = nullptr;
    std::chrono::steady_clock::time_point g_last;

    void __fastcall UpdateDetour(void* self, void* unused) {
        g_originalUpdate(self, unused);

        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_last).count();
        g_last = now;
        g_tick(seconds > kMaxSeconds ? 0.0f : seconds);
    }
}

namespace Flashlight::Frame {

bool Install(TickFn tick) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update) {
        api->Log("frame: CXGame::Update was not found in this build");
        return false;
    }
    g_tick = tick;
    return api->Hook(reinterpret_cast<void*>(g_update.address()), reinterpret_cast<void*>(&UpdateDetour),
                     reinterpret_cast<void**>(&g_originalUpdate));
}

}
