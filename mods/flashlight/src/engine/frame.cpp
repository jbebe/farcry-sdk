// The end of the engine's frame, as somewhere to run code once a frame on the game thread.
#include "engine/frame.h"

#include "fcse_api.h"

#include <chrono>
#include <cstdint>

namespace {
    // CXGame::Update's last three calls on one object, then
    // `pop edi; pop esi; mov esp, ebp; pop ebp; ret`.
    FCSE::Relocation<uint8_t*> g_tail{FCSE::Pattern(
        "8B 42 4C 6A 01 FF D0 8B 0D ?? ?? ?? ?? 6A 00 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 6A 01 E8 ?? "
        "?? ?? ?? 8B 0D ?? ?? ?? ?? 6A 04 E8 ?? ?? ?? ?? 5F 5E 8B E5 5D C3")};

    constexpr size_t kEpilogue = 46;

    // Longer steps, a hitch or a load, count as no time at all.
    constexpr float kMaxSeconds = 0.25f;

    Flashlight::Frame::TickFn g_tick = nullptr;
    std::chrono::steady_clock::time_point g_last;

    void OnTail(FCSE_MidHookContext*) {
        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_last).count();
        g_last = now;
        g_tick(seconds > kMaxSeconds ? 0.0f : seconds);
    }
}

namespace Flashlight::Frame {

bool Install(TickFn tick) {
    if (!g_tail) {
        FCSE::ApiPointer()->Log("frame: CXGame::Update's tail was not found in this build");
        return false;
    }
    g_tick = tick;
    return FCSE::ApiPointer()->MidHook(g_tail.get() + kEpilogue, &OnTail);
}

}
