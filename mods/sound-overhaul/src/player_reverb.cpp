// Player reverb: raises the reverb send of 2D voices (unpositioned or stereo), the player's own
// sounds among them.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // `add eax, -10000` turning a voice's send (EAX: 0 or kFullSend) into EAXSOURCE_ROOM; EDI is
    // the voice.
    FCSE::Relocation<uint8_t*> g_roomLevel{FCSE::Pattern(
        "05 F0 D8 FF FF 6A 00 89 44 24 18 8B 87 04 01 00 00 8B 08 6A 07")};

    // The voice's IDirectSound3DBuffer, null for a 2D voice.
    constexpr uintptr_t kBuffer3D = 0x6C;
    constexpr uintptr_t kFullSend = 10000;
    // mB above a full send; EAXSOURCE_MAXROOM allows no more.
    constexpr unsigned kBoost = 1000;

    void OnRoomLevel(FCSE_MidHookContext* ctx) {
        if (ctx->eax == kFullSend && *reinterpret_cast<void**>(ctx->edi + kBuffer3D) == nullptr) {
            ctx->eax += kBoost;
        }
    }
}

void ApplyPlayerReverb() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_roomLevel) {
        api->Log("player reverb: DARE's room send was not found in this build");
        return;
    }

    if (api->MidHook(g_roomLevel.get(), &OnRoomLevel)) {
        FCSE::Logf("player reverb: 2D voices' reverb send raised by %u mB", kBoost);
    }
}
