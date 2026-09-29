// Sound delay: an NPC's shot and an explosion are heard when their sound reaches the listener, their
// distance / 340 m/s after the game plays them. CSoundSystem's play calls of those two types are held
// back and replayed from its Update. A held play returns a stand-in handle, which StopSound and
// IsSoundPlaying understand.
#include "sound_system.h"

#include "fcse_api.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>

namespace {
    using namespace SoundOverhaul::SoundSystem;

    constexpr int kWeaponNpc = 9;
    constexpr int kExplosion = 10;

    // DARE's own, for Doppler.
    constexpr float kSpeedOfSound = 340.0f;
    // Nearer than 10 m plays at once; beyond 1 km is heard as at 1 km.
    constexpr double kMinDelay = 0.03;
    constexpr double kMaxDelay = 3.0;

    constexpr uint32_t kNoSound = 0xFFFFFFFF;
    // Stand-in handles are this tag over a 24-bit count; DARE's own count up from 0.
    constexpr uint32_t kStandInTag = 0xC0000000;
    constexpr uint32_t kStandInMask = 0xFF000000;

    // ISoundObjectCallbacks::GetPosition(vec3& out).
    constexpr size_t kGetPosition = 0x04 / sizeof(void*);
    // SSoundAnswerList: a reference count at +4, and Release at vtable +4 once it reaches 0.
    constexpr uintptr_t kReferences = 0x4;
    constexpr size_t kRelease = 0x04 / sizeof(void*);

    struct Vec3 {
        float x, y, z;
    };

    using PlaySoundFn = uint32_t(__thiscall*)(void* system, uint32_t id, int type, void* callbacks, float volume);
    using PlaySoundAtPositionFn = uint32_t(__thiscall*)(void* system, uint32_t id, int type, const Vec3* position,
                                                        void* answers, float volume);
    using StopSoundFn = void(__thiscall*)(void* system, uint32_t handle, float fade, uint32_t flags);
    using IsSoundPlayingFn = bool(__thiscall*)(void* system, uint32_t handle);
    using StopAllSoundsFn = void(__thiscall*)(void* system);
    using StopAllSoundsOfFn = void(__thiscall*)(void* system, void* callbacks, float fade);
    using StopAllSoundsOfTypeFn = void(__thiscall*)(void* system, int type, float fade);
    using UpdateFn = void(__thiscall*)(void* system, float seconds, int unknown);
    using GetListenerCallbacksFn = void*(__thiscall*)(void* system);
    using GetPositionFn = void(__thiscall*)(void* callbacks, Vec3* out);
    using ReleaseFn = void(__thiscall*)(void* object);

    PlaySoundFn g_playSound = nullptr;
    PlaySoundAtPositionFn g_playSoundAtPosition = nullptr;
    PlaySoundFn g_playSoundStayAlive = nullptr;
    StopSoundFn g_stopSound = nullptr;
    IsSoundPlayingFn g_isSoundPlaying = nullptr;
    StopAllSoundsFn g_stopAllSounds = nullptr;
    StopAllSoundsOfFn g_stopAllSoundsOf = nullptr;
    StopAllSoundsOfTypeFn g_stopAllSoundsOfType = nullptr;
    UpdateFn g_update = nullptr;

    enum class Kind : uint8_t { Free, Play, PlayAtPosition, Stop };

    // A held call. A Stop is one made on a stand-in while its play was still held, held as long.
    struct Held {
        Kind kind = Kind::Free;
        uint32_t order = 0;
        double due = 0.0;
        double delay = 0.0;
        uint32_t handle = kNoSound;
        uint32_t id = kNoSound;
        int type = 0;
        void* callbacks = nullptr;
        Vec3 position{};
        void* answers = nullptr;
        float volume = 0.0f;
        float fade = 0.0f;
        uint32_t flags = 0;
    };
    std::array<Held, 128> g_held;
    uint32_t g_nextOrder = 0;
    uint32_t g_nextStandIn = 0;

    // The real handle of each recently replayed stand-in, at its low byte.
    struct Played {
        uint32_t standIn = kNoSound;
        uint32_t handle = kNoSound;
    };
    std::array<Played, 256> g_played;

    // Seconds of sound-system updates.
    double g_clock = 0.0;
    // Set while an original play call runs, so the PlaySound it makes inside is not held again.
    int g_inPlay = 0;

    bool IsStandIn(uint32_t handle) {
        return (handle & kStandInMask) == kStandInTag;
    }

    Vec3 PositionOf(void* callbacks) {
        Vec3 position{};
        reinterpret_cast<GetPositionFn>((*static_cast<void***>(callbacks))[kGetPosition])(callbacks, &position);
        return position;
    }

    // How long `type`'s sound at `position` takes to reach the listener, or 0 to play it now.
    double DelayOf(void* system, int type, const Vec3& position) {
        if (type != kWeaponNpc && type != kExplosion) {
            return 0.0;
        }
        // An emitter with no entity reports the origin.
        if (position.x == 0.0f && position.y == 0.0f && position.z == 0.0f) {
            return 0.0;
        }
        void* listener =
            reinterpret_cast<GetListenerCallbacksFn>((*static_cast<void***>(system))[kGetListenerCallbacks])(system);
        if (listener == nullptr) {
            return 0.0;
        }
        const Vec3 at = PositionOf(listener);
        const double x = position.x - at.x;
        const double y = position.y - at.y;
        const double z = position.z - at.z;
        const double delay = std::sqrt(x * x + y * y + z * z) / kSpeedOfSound;
        return delay < kMinDelay ? 0.0 : (std::min)(delay, kMaxDelay);
    }

    Held* FreeSlot() {
        for (Held& held : g_held) {
            if (held.kind == Kind::Free) {
                return &held;
            }
        }
        return nullptr;
    }

    Held* HeldPlay(uint32_t standIn) {
        for (Held& held : g_held) {
            if ((held.kind == Kind::Play || held.kind == Kind::PlayAtPosition) && held.handle == standIn) {
                return &held;
            }
        }
        return nullptr;
    }

    uint32_t Resolve(uint32_t standIn) {
        const Played& played = g_played[standIn & 0xFF];
        return played.standIn == standIn ? played.handle : kNoSound;
    }

    void Drop(Held& held) {
        if (held.answers != nullptr && --*reinterpret_cast<int*>(static_cast<uint8_t*>(held.answers) + kReferences) == 0) {
            reinterpret_cast<ReleaseFn>((*static_cast<void***>(held.answers))[kRelease])(held.answers);
        }
        held = Held{};
    }

    // Holds `call` for `delay` seconds; returns the stand-in it acts on, a new one for a play, or kNoSound
    // when the queue is full.
    uint32_t Hold(Held call, double delay) {
        Held* slot = FreeSlot();
        if (slot == nullptr) {
            return kNoSound;
        }
        if (call.answers != nullptr) {
            ++*reinterpret_cast<int*>(static_cast<uint8_t*>(call.answers) + kReferences);
        }
        call.order = g_nextOrder++;
        call.due = g_clock + delay;
        call.delay = delay;
        if (call.kind != Kind::Stop) {
            call.handle = kStandInTag | (g_nextStandIn++ & ~kStandInMask);
        }
        *slot = call;
        return slot->handle;
    }

    void Replay(void* system, Held& held) {
        if (held.kind == Kind::Stop) {
            const uint32_t handle = Resolve(held.handle);
            if (handle != kNoSound) {
                g_stopSound(system, handle, held.fade, held.flags);
            }
        } else {
            ++g_inPlay;
            const uint32_t handle =
                held.kind == Kind::Play
                    ? g_playSound(system, held.id, held.type, held.callbacks, held.volume)
                    : g_playSoundAtPosition(system, held.id, held.type, &held.position, held.answers, held.volume);
            --g_inPlay;
            g_played[held.handle & 0xFF] = {held.handle, handle};
        }
        Drop(held);
    }

    uint32_t __fastcall OnPlaySound(void* system, void*, uint32_t id, int type, void* callbacks, float volume) {
        if (g_inPlay == 0 && id != kNoSound && callbacks != nullptr) {
            const double delay = DelayOf(system, type, PositionOf(callbacks));
            if (delay > 0.0) {
                Held call;
                call.kind = Kind::Play;
                call.id = id;
                call.type = type;
                call.callbacks = callbacks;
                call.volume = volume;
                const uint32_t standIn = Hold(call, delay);
                if (standIn != kNoSound) {
                    return standIn;
                }
            }
        }
        return g_playSound(system, id, type, callbacks, volume);
    }

    uint32_t __fastcall OnPlaySoundAtPosition(void* system, void*, uint32_t id, int type, const Vec3* position,
                                            void* answers, float volume) {
        const double delay = id != kNoSound ? DelayOf(system, type, *position) : 0.0;
        if (delay > 0.0) {
            Held call;
            call.kind = Kind::PlayAtPosition;
            call.id = id;
            call.type = type;
            call.position = *position;
            call.answers = answers;
            call.volume = volume;
            const uint32_t standIn = Hold(call, delay);
            if (standIn != kNoSound) {
                return standIn;
            }
        }
        ++g_inPlay;
        const uint32_t handle = g_playSoundAtPosition(system, id, type, position, answers, volume);
        --g_inPlay;
        return handle;
    }

    uint32_t __fastcall OnPlaySoundStayAlive(void* system, void*, uint32_t id, int type, void* callbacks, float volume) {
        ++g_inPlay;
        const uint32_t handle = g_playSoundStayAlive(system, id, type, callbacks, volume);
        --g_inPlay;
        return handle;
    }

    void __fastcall OnStopSound(void* system, void*, uint32_t handle, float fade, uint32_t flags) {
        if (!IsStandIn(handle)) {
            g_stopSound(system, handle, fade, flags);
            return;
        }
        if (const Held* play = HeldPlay(handle)) {
            Held stop;
            stop.kind = Kind::Stop;
            stop.handle = handle;
            stop.fade = fade;
            stop.flags = flags;
            Hold(stop, play->delay);
            return;
        }
        const uint32_t real = Resolve(handle);
        if (real != kNoSound) {
            g_stopSound(system, real, fade, flags);
        }
    }

    bool __fastcall OnIsSoundPlaying(void* system, void*, uint32_t handle) {
        if (!IsStandIn(handle)) {
            return g_isSoundPlaying(system, handle);
        }
        if (HeldPlay(handle) != nullptr) {
            return true;
        }
        const uint32_t real = Resolve(handle);
        return real != kNoSound && g_isSoundPlaying(system, real);
    }

    void __fastcall OnStopAllSounds(void* system, void*) {
        for (Held& held : g_held) {
            Drop(held);
        }
        g_stopAllSounds(system);
    }

    // Every ISoundObjectCallbacks calls this as it is destroyed, so no held play outlives its emitter.
    void __fastcall OnStopAllSoundsOf(void* system, void*, void* callbacks, float fade) {
        for (Held& held : g_held) {
            if (held.kind == Kind::Play && held.callbacks == callbacks) {
                Drop(held);
            }
        }
        g_stopAllSoundsOf(system, callbacks, fade);
    }

    void __fastcall OnStopAllSoundsOfType(void* system, void*, int type, float fade) {
        for (Held& held : g_held) {
            if ((held.kind == Kind::Play || held.kind == Kind::PlayAtPosition) && held.type == type) {
                Drop(held);
            }
        }
        g_stopAllSoundsOfType(system, type, fade);
    }

    void __fastcall OnUpdate(void* system, void*, float seconds, int unknown) {
        g_update(system, seconds, unknown);
        g_clock += seconds;
        for (;;) {
            Held* next = nullptr;
            for (Held& held : g_held) {
                if (held.kind != Kind::Free && held.due <= g_clock &&
                    (next == nullptr || held.order < next->order)) {
                    next = &held;
                }
            }
            if (next == nullptr) {
                return;
            }
            Replay(system, *next);
        }
    }

    struct Slot {
        size_t index;
        void* hook;
        void** original;
    };
}

void ApplySoundDelay() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void** vtable = Vtable();
    if (vtable == nullptr) {
        api->Log("sound delay: CSoundSystem's constructor was not found in this build - sounds are not delayed");
        return;
    }

    const Slot slots[] = {
        {kPlaySound, reinterpret_cast<void*>(&OnPlaySound), reinterpret_cast<void**>(&g_playSound)},
        {kPlaySoundAtPosition, reinterpret_cast<void*>(&OnPlaySoundAtPosition),
         reinterpret_cast<void**>(&g_playSoundAtPosition)},
        {kPlaySoundStayAlive, reinterpret_cast<void*>(&OnPlaySoundStayAlive),
         reinterpret_cast<void**>(&g_playSoundStayAlive)},
        {kStopSound, reinterpret_cast<void*>(&OnStopSound), reinterpret_cast<void**>(&g_stopSound)},
        {kIsSoundPlaying, reinterpret_cast<void*>(&OnIsSoundPlaying), reinterpret_cast<void**>(&g_isSoundPlaying)},
        {kStopAllSounds, reinterpret_cast<void*>(&OnStopAllSounds), reinterpret_cast<void**>(&g_stopAllSounds)},
        {kStopAllSoundsOf, reinterpret_cast<void*>(&OnStopAllSoundsOf), reinterpret_cast<void**>(&g_stopAllSoundsOf)},
        {kStopAllSoundsOfType, reinterpret_cast<void*>(&OnStopAllSoundsOfType),
         reinterpret_cast<void**>(&g_stopAllSoundsOfType)},
        {kUpdate, reinterpret_cast<void*>(&OnUpdate), reinterpret_cast<void**>(&g_update)},
    };
    for (const Slot& slot : slots) {
        *slot.original = vtable[slot.index];
    }
    for (const Slot& slot : slots) {
        if (!api->Patch(&vtable[slot.index], &slot.hook, sizeof(slot.hook))) {
            FCSE::Logf("sound delay: vtable slot +0x%02X could not be patched", slot.index * sizeof(void*));
        }
    }
    api->Log("sound delay: NPC shots and explosions reach the listener at 340 m/s");
}
