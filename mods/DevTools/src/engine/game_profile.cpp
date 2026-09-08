// The cheat flags the game profile carries.
#include "engine/game_profile.h"

#include "engine/pawn_tick.h"
#include "fcse_api.h"

namespace {
    // The pointer is read out of the MOV EAX,[imm32] that opens CWeaponBazaar::IsWeaponUnlocked,
    // where the CMP that follows names the AllWeaponsUnlock field and makes the pattern unique.
    FCSE::Relocation<uint8_t*> g_profileLoad{FCSE::Pattern("A1 ?? ?? ?? ?? 83 B8 A0 00 00 00 00")};

    constexpr size_t kProfileOperand = 1;

    constexpr size_t kCheatCount = static_cast<size_t>(DevTools::GameProfile::Cheat::Count);
    constexpr ptrdiff_t kOffsets[kCheatCount] = {0x94, 0x98, 0xA0};

    void** g_profile = nullptr;
    bool g_wanted[kCheatCount]{};

    void OnTick(const DevTools::PawnTick::Frame&) {
        auto* profile = static_cast<uint8_t*>(g_profile != nullptr ? *g_profile : nullptr);
        if (profile == nullptr) {
            return;
        }

        for (size_t i = 0; i < kCheatCount; ++i) {
            // Consumers compare unsigned, so anything but 1 is left alone rather than normalised.
            if (g_wanted[i]) {
                *reinterpret_cast<int32_t*>(profile + kOffsets[i]) = 1;
            }
        }
    }
}

namespace DevTools::GameProfile {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_profileLoad) {
        api->Log("game profile: the profile pointer was not found in this build - the cheat flags "
                 "cannot be set");
        return;
    }

    g_profile = *reinterpret_cast<void***>(g_profileLoad.get() + kProfileOperand);
    PawnTick::Subscribe(&OnTick);
}

void Want(Cheat cheat, bool on) {
    g_wanted[static_cast<size_t>(cheat)] = on;

    // Turning off writes the zero here rather than on the next frame: if there is no profile yet
    // there is nothing to clear, which is exactly the flag this plugin never wrote.
    if (!on) {
        if (int32_t* field = Field(cheat)) {
            *field = 0;
        }
    }
}

int32_t* Field(Cheat cheat) {
    if (g_profile == nullptr || *g_profile == nullptr) {
        return nullptr;
    }
    return reinterpret_cast<int32_t*>(static_cast<uint8_t*>(*g_profile) + kOffsets[static_cast<size_t>(cheat)]);
}

}
