// Echoes: NPC shots play the weapon's echo too, which the game only does for the player's - its NPC version
// at the echo's id + 0x10 when that bank exists, held loaded here - and of the echoes a burst plays, one per
// round, only the last rings out: each new round's echo fades out the previous one from the same shooter.
#include "mutes.h"

#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <cstdint>

namespace {
    // ApplyDelayBullet storing the handle of the player's echo (EAX), before it fetches the sound system,
    // reads the echo length and stops the echo over it.
    FCSE::Relocation<uint8_t*> g_playerEcho{FCSE::Pattern(
        "89 44 24 30 E8 ?? ?? ?? ?? 8B 30 8B 0D ?? ?? ?? ?? 6A 00 89 44 24 60 81 C6 A8 00 00 00 E8")};

    // The third-person branch fetching the sound system, right before PlaySound(+0x47C, +0x488, +0x110).
    FCSE::Relocation<uint8_t*> g_npcShot{FCSE::Pattern(
        "E8 ?? ?? ?? ?? D9 EE 8B 4F 50 8B 10 8B 92 9C 00 00 00 51 D9 1C 24 56 8B B1 88 04 00 00 "
        "8B 89 7C 04 00 00")};

    // CSoundResource::GetFromSoundId: a counted reference to the bank named after a sound id, created
    // unloaded when nothing holds it yet.
    FCSE::Relocation<uint8_t*> g_getFromSoundId{FCSE::Pattern(
        "8B 44 24 08 81 EC 0C 01 00 00 3B 05 ?? ?? ?? ?? 74 4B 8B 8C 24 18 01 00 00 51 50 8D 54 24 10 52 "
        "E8 ?? ?? ?? ?? 83 C4 0C 84 C0 74 31 56 8B 35 ?? ?? ?? ?? 57")};

    // In g_playerEcho: `call GetSoundSystem`, the ambiance manager in `mov ecx, [..]`, and
    // `call CAmbianceManager::GetEchoLength`.
    constexpr uintptr_t kGetSoundSystemCall = 4;
    constexpr uintptr_t kAmbianceManager = 13;
    constexpr uintptr_t kGetEchoLengthCall = 29;

    constexpr uintptr_t kFireProperties = 0x50;
    constexpr uintptr_t kEcho = 0x490;
    constexpr uintptr_t kThirdPersonSingleShotType = 0x488;
    // The delayed bullet in EBX starts with its origin at +4.
    constexpr uintptr_t kShotOrigin = 0x4;
    constexpr uint32_t kNoSound = 0xFFFFFFFF;
    // An echo bank may hold an NPC version of its echo at this offset: positioned, so it fades with distance,
    // which the player's own echo cannot be, as it plays through a first-person sound type.
    constexpr uint32_t kNpcEchoOffset = 0x10;

    constexpr uintptr_t kPlaySoundAtPosition = 0xA0;
    constexpr uintptr_t kStopSound = 0xA8;

    // Echoes from one shooter closer together than this belong to one burst.
    constexpr ULONGLONG kBurstGapMs = 350;
    constexpr float kCutFadeSeconds = 0.03f;

    using GetSoundSystemFn = void* (*)();
    using GetEchoLengthFn = float(__thiscall*)(void* ambiance);
    using PlaySoundAtPositionFn = uint32_t(__thiscall*)(void* system, uint32_t id, int32_t type,
                                                        const void* position, uint32_t unused, float volume);
    using StopSoundFn = void(__thiscall*)(void* system, uint32_t handle, float fade, uint32_t flags);

    struct SoundResourceRef {
        void* resource = nullptr;
        bool requested = false;
    };
    using GetFromSoundIdFn = SoundResourceRef*(__cdecl*)(SoundResourceRef* out, uint32_t id, const char* language);
    using RequestLoadFn = void(__thiscall*)(void* resource);
    constexpr uintptr_t kRequestLoad = 0x8;

    // The NPC echo banks held loaded. Nothing in the data names them, and a play loads nothing: it fails
    // until a reference has requested the bank.
    std::array<uint32_t, 8> g_heldEchoes{};

    GetSoundSystemFn g_getSoundSystem = nullptr;
    GetEchoLengthFn g_getEchoLength = nullptr;
    void** g_ambianceManager = nullptr;

    // The latest echo of each recent shooter, keyed by its fire strategy.
    struct Burst {
        uintptr_t strategy = 0;
        uint32_t echo = kNoSound;
        ULONGLONG time = 0;
    };
    std::array<Burst, 32> g_bursts;

    template <typename Fn>
    Fn Slot(void* system, uintptr_t offset) {
        return reinterpret_cast<Fn>((*reinterpret_cast<void***>(system))[offset / sizeof(void*)]);
    }

    const uint8_t* CallTarget(const uint8_t* call) {
        return call + 5 + *reinterpret_cast<const int32_t*>(call + 1);
    }

    // Takes a reference to `id`'s bank and requests it, once, and never lets it go.
    void HoldLoaded(uint32_t id) {
        if (!g_getFromSoundId ||
            std::find(g_heldEchoes.begin(), g_heldEchoes.end(), id) != g_heldEchoes.end()) {
            return;
        }
        const auto free = std::find(g_heldEchoes.begin(), g_heldEchoes.end(), 0u);
        if (free == g_heldEchoes.end()) {
            return;
        }
        *free = id;

        SoundResourceRef ref;
        reinterpret_cast<GetFromSoundIdFn>(g_getFromSoundId.get())(&ref, id, nullptr);
        if (ref.resource != nullptr) {
            Slot<RequestLoadFn>(ref.resource, kRequestLoad)(ref.resource);
        }
    }

    void OnEcho(uintptr_t strategy, uint32_t echo) {
        const ULONGLONG now = GetTickCount64();
        Burst* slot = &g_bursts[0];
        for (Burst& burst : g_bursts) {
            if (burst.strategy == strategy) {
                slot = &burst;
                break;
            }
            if (burst.time < slot->time) {
                slot = &burst;
            }
        }

        if (slot->strategy == strategy && now - slot->time < kBurstGapMs && slot->echo != 0 &&
            slot->echo != kNoSound) {
            void* system = g_getSoundSystem();
            Slot<StopSoundFn>(system, kStopSound)(system, slot->echo, kCutFadeSeconds, 0);
        }

        *slot = {strategy, echo, now};
    }

    void OnPlayerEcho(FCSE_MidHookContext* ctx) {
        if (SoundOverhaul::Mutes::echoes) {
            void* system = g_getSoundSystem();
            Slot<StopSoundFn>(system, kStopSound)(system, static_cast<uint32_t>(ctx->eax), 0.0f, 0);
            return;
        }
        OnEcho(ctx->edi, static_cast<uint32_t>(ctx->eax));
    }

    void OnNpcShot(FCSE_MidHookContext* ctx) {
        const uintptr_t properties = *reinterpret_cast<const uintptr_t*>(ctx->edi + kFireProperties);
        const uint32_t echoId = *reinterpret_cast<const uint32_t*>(properties + kEcho);
        if (echoId == kNoSound || SoundOverhaul::Mutes::echoes) {
            return;
        }

        void* system = g_getSoundSystem();
        const auto play = [&](uint32_t id) {
            return Slot<PlaySoundAtPositionFn>(system, kPlaySoundAtPosition)(
                system, id, *reinterpret_cast<const int32_t*>(properties + kThirdPersonSingleShotType),
                reinterpret_cast<const void*>(ctx->ebx + kShotOrigin), 0, 0.0f);
        };
        HoldLoaded(echoId + kNpcEchoOffset);
        uint32_t echo = play(echoId + kNpcEchoOffset);
        if (echo == kNoSound) {
            echo = play(echoId);
        }
        Slot<StopSoundFn>(system, kStopSound)(system, echo, g_getEchoLength(*g_ambianceManager), 0);
        OnEcho(ctx->edi, echo);
    }
}

void ApplyLastEcho() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_playerEcho || !g_npcShot) {
        api->Log("echoes: ApplyDelayBullet's echo was not found in this build");
        return;
    }

    const uint8_t* site = g_playerEcho.get();
    g_getSoundSystem = reinterpret_cast<GetSoundSystemFn>(CallTarget(site + kGetSoundSystemCall));
    g_getEchoLength = reinterpret_cast<GetEchoLengthFn>(CallTarget(site + kGetEchoLengthCall));
    g_ambianceManager = *reinterpret_cast<void** const*>(site + kAmbianceManager);

    if (!g_getFromSoundId) {
        api->Log("echoes: GetFromSoundId was not found in this build - NPC echoes fall back to the player's");
    }

    if (api->MidHook(g_playerEcho.get(), &OnPlayerEcho) && api->MidHook(g_npcShot.get(), &OnNpcShot)) {
        api->Log("echoes: NPC shots echo, and a burst's echo rings out only after its last round");
    }
}
