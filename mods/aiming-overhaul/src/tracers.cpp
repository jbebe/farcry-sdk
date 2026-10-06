// A shot decides on its tracer in CWeaponFireBulletStrategy::ApplyDelayBullet, which first skips it
// for a shooter drawn in first person. The hook sits on that test: a weapon not listed jumps past
// the tracer, and a listed one past the test alone. CBulletTracerManager then keeps the tracer: it
// is added slower and shorter than the weapon says, so it is seen crossing the distance, some
// ricochet up into the air when they get there, and each frame's streak is drawn no thinner than a
// few pixels, however far off.
//
// See docs/docs/engine-internals/bullet-tracers.md.
#include "tracers.h"

#include "engine/aim.h"
#include "engine/entity_name.h"
#include "fcse_api.h"
#include "streak.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <numbers>
#include <random>
#include <string_view>

namespace {
    // What the entity names of the weapons that leave tracers start with: an archetype, with its
    // variants under it, or a family of mounted guns.
    constexpr std::string_view kTracerWeapons[] = {
        "weapons.Special.M249_Saw",
        "weapons.Special.PKM",
        "weapons.Primary.Dragunov",
        "weapons.Primary.AS50",
        "weapons.MountedWeapons.M249_",
        "weapons.MountedWeapons.M2_",
    };

    // The test of the shooter's first-person flag and the long jump past the tracer it takes, with
    // the fire strategy in EDI; and the strategy's weapon.
    FCSE::Relocation<uint8_t*> g_firstPersonTest{FCSE::Pattern(
        "F6 40 04 80 0F 85 ?? ?? ?? ?? 8B 87 68 01 00 00 85 C0 0F 84 ?? ?? ?? ?? 8B 4F 50")};
    constexpr ptrdiff_t kJump = 4;
    constexpr ptrdiff_t kJumpLength = 6;
    constexpr ptrdiff_t kStrategyWeapon = 0x40;

    // CBulletTracerManager, all __thiscall: AddTrace(start, end, speed, length, distortion length,
    // width, &texture, &distortion texture); Update(seconds, a second argument it never reads),
    // which draws and moves its traces and frees those whose time is up; and BuildTrace(trace,
    // camera position), which pops both and leaves ECX alone.
    using AddTraceFn = void(__fastcall*)(uint8_t* manager, void* unused, const float* start,
                                         const float* end, float speed, float length,
                                         float distortionLength, float width,
                                         const uint32_t* texture,
                                         const uint32_t* distortionTexture);
    using UpdateFn = void(__fastcall*)(uint8_t* manager, void* unused, float seconds,
                                       uint32_t unread);
    using BuildTraceFn = void(__fastcall*)(void* unused, void* unusedEdx, uint8_t* trace,
                                           const float* camera);
    FCSE::Relocation<AddTraceFn> g_addTrace{FCSE::Uplay(0x00102530)};
    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x001022D0)};
    FCSE::Relocation<BuildTraceFn> g_buildTrace{FCSE::Uplay(0x00101960)};
    AddTraceFn g_originalAddTrace = nullptr;
    UpdateFn g_originalUpdate = nullptr;
    BuildTraceFn g_originalBuildTrace = nullptr;

    // The manager's traces, as AddTrace's `this` sees them: a pointer to the first and a count.
    constexpr ptrdiff_t kTraces = 0xE0;
    constexpr ptrdiff_t kTraceCount = 0xE4;
    // A trace: the end it flies to, the rear of its streak, which runs forward from it toward the
    // end and is the start when added, the streak's width and longest length, and its textures.
    constexpr ptrdiff_t kTraceEnd = 0x0C;
    constexpr ptrdiff_t kTraceRear = 0x18;
    constexpr ptrdiff_t kTraceWidth = 0x48;
    constexpr ptrdiff_t kTraceLength = 0x50;
    constexpr ptrdiff_t kTraceDistortionLength = 0x54;
    constexpr ptrdiff_t kTraceTexture = 0x58;
    constexpr ptrdiff_t kTraceDistortionTexture = 0x60;

    // A tracer's speed, and its streak's longest length and its greatest share of the shot, in
    // metres. A streak is drawn only until its front reaches the end, so a shot nearer than the
    // streak is long would show for one frame; and one shorter than a frame's flight is seen in
    // separate dashes.
    constexpr float kSpeed = 350.0f;
    constexpr float kLength = 8.0f;
    constexpr float kLengthShare = 0.4f;

    // Every how many tracers one ricochets, the farthest a shot can end for it to, since a miss
    // ends in the air at the weapon's range, and the nearest the camera, since that is a shot at
    // the player. A ricochet flies slower and shorter, anywhere from level to straight up and from
    // one side of onward to the other, never back.
    constexpr uint32_t kRicochetEvery = 6;
    constexpr float kRicochetFarthestShot = 150.0f;
    constexpr float kRicochetNearestCamera = 3.0f;
    constexpr float kRicochetSpeed = kSpeed / 2.0f;
    constexpr float kRicochetLength = 4.0f;
    constexpr float kRicochetNearest = 20.0f;
    constexpr float kRicochetFarthest = 45.0f;
    constexpr float kRicochetLowest = 0.0f;
    constexpr float kRicochetHighest = std::numbers::pi_v<float> / 2.0f;
    constexpr float kRicochetSpread = std::numbers::pi_v<float> / 2.0f;

    // The least width a streak is drawn at, in radians of an unmagnified view: about six pixels
    // at 1080p, where the engine's few centimetres are under one beyond 20 m.
    constexpr float kLeastWidth = 0.006f;

    std::atomic<bool> g_enabled{true};
    uintptr_t g_pastTest = 0;
    uintptr_t g_pastTracer = 0;

    // A ricochet waiting for its tracer to reach the end: the seconds left, the tracer, which it
    // takes its place and textures from while the manager still holds it, and where it flies to
    // from there.
    struct Ricochet {
        float due;
        const uint8_t* trace;
        float flight[3];
    };
    constexpr size_t kMostRicochets = 64;
    Ricochet g_ricochets[kMostRicochets];
    size_t g_ricochetCount = 0;
    uint8_t* g_manager = nullptr;
    uint32_t g_tracers = 0;
    // Where the camera was when a streak was last drawn.
    float g_camera[3] = {};
    std::minstd_rand g_random{20261005};

    template <class T>
    T& Field(uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(object + offset);
    }

    float Length(const float* v) {
        return std::sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    }

    bool LeavesTracers(uint8_t* weapon) {
        char name[64];
        AimingOverhaul::EntityName::Read(weapon, name);
        return std::ranges::any_of(kTracerWeapons, [&](std::string_view start) {
            return std::string_view(name).starts_with(start);
        });
    }

    void OnFirstPersonTest(FCSE_MidHookContext* ctx) {
        if (!g_enabled) {
            return;
        }
        uint8_t* weapon = *reinterpret_cast<uint8_t**>(ctx->edi + kStrategyWeapon);
        ctx->eip = LeavesTracers(weapon) ? g_pastTest : g_pastTracer;
    }

    // Where a ricochet off the end of a shot flies, from that end.
    void Bounce(const float* start, const float* end, float (&flight)[3]) {
        std::uniform_real_distribution<float> unit(0.0f, 1.0f);
        const float heading = std::atan2(end[1] - start[1], end[0] - start[0]) +
                              (unit(g_random) * 2.0f - 1.0f) * kRicochetSpread;
        const float climb =
            kRicochetLowest + unit(g_random) * (kRicochetHighest - kRicochetLowest);
        const float reach =
            kRicochetNearest + unit(g_random) * (kRicochetFarthest - kRicochetNearest);
        flight[0] = std::cos(climb) * std::cos(heading) * reach;
        flight[1] = std::cos(climb) * std::sin(heading) * reach;
        flight[2] = std::sin(climb) * reach;
    }

    void __fastcall AddTraceDetour(uint8_t* manager, void* unused, const float* start,
                                   const float* end, float speed, float length,
                                   float distortionLength, float width, const uint32_t* texture,
                                   const uint32_t* distortionTexture) {
        if (!g_enabled) {
            g_originalAddTrace(manager, unused, start, end, speed, length, distortionLength, width,
                               texture, distortionTexture);
            return;
        }
        const float shot[3] = {end[0] - start[0], end[1] - start[1], end[2] - start[2]};
        const float distance = Length(shot);
        const float streak = (std::min)(kLength, distance * kLengthShare);
        g_originalAddTrace(manager, unused, start, end, kSpeed, streak, distortionLength, width,
                           texture, distortionTexture);
        g_manager = manager;
        const uint32_t count = Field<uint32_t>(manager, kTraceCount);
        if (++g_tracers % kRicochetEvery != 0 || distance >= kRicochetFarthestShot || count == 0 ||
            g_ricochetCount == kMostRicochets) {
            return;
        }
        Ricochet& ricochet = g_ricochets[g_ricochetCount++];
        ricochet.due = (distance - streak) / kSpeed;
        ricochet.trace = Field<uint8_t**>(manager, kTraces)[count - 1];
        Bounce(start, end, ricochet.flight);
    }

    // Whether the manager still holds the trace, so its textures are still alive.
    bool Holds(uint8_t* manager, const uint8_t* trace) {
        uint8_t** traces = Field<uint8_t**>(manager, kTraces);
        return std::find(traces, traces + Field<uint32_t>(manager, kTraceCount), trace) !=
               traces + Field<uint32_t>(manager, kTraceCount);
    }

    // Ricochets are added before the manager walks its traces, which adding one would move under
    // it.
    void __fastcall UpdateDetour(uint8_t* manager, void* unused, float seconds, uint32_t unread) {
        size_t waiting = 0;
        for (size_t i = 0; i < g_ricochetCount; i++) {
            Ricochet ricochet = g_ricochets[i];
            ricochet.due -= seconds;
            if (ricochet.due > 0.0f) {
                g_ricochets[waiting++] = ricochet;
                continue;
            }
            if (g_manager == nullptr || !Holds(g_manager, ricochet.trace)) {
                continue;
            }
            uint8_t* trace = const_cast<uint8_t*>(ricochet.trace);
            const float* from = &Field<float>(trace, kTraceEnd);
            const float toCamera[3] = {g_camera[0] - from[0], g_camera[1] - from[1],
                                       g_camera[2] - from[2]};
            if (Length(toCamera) < kRicochetNearestCamera) {
                continue;
            }
            const float to[3] = {from[0] + ricochet.flight[0], from[1] + ricochet.flight[1],
                                 from[2] + ricochet.flight[2]};
            g_originalAddTrace(g_manager, unused, from, to, kRicochetSpeed, kRicochetLength,
                               Field<float>(trace, kTraceDistortionLength),
                               Field<float>(trace, kTraceWidth),
                               &Field<uint32_t>(trace, kTraceTexture),
                               &Field<uint32_t>(trace, kTraceDistortionTexture));
        }
        g_ricochetCount = waiting;
        g_originalUpdate(manager, unused, seconds, unread);
    }

    // How far the camera is from the nearest point of the trace's streak.
    float Distance(uint8_t* trace, const float* camera) {
        const float* rear = &Field<float>(trace, kTraceRear);
        const float* end = &Field<float>(trace, kTraceEnd);
        float along[3];
        float toCamera[3];
        for (int i = 0; i < 3; i++) {
            along[i] = end[i] - rear[i];
            toCamera[i] = camera[i] - rear[i];
        }
        const float span = Length(along);
        const float length = (std::min)(span, Field<float>(trace, kTraceLength));
        const float reach =
            span > 0.0f ? (along[0] * toCamera[0] + along[1] * toCamera[1] +
                           along[2] * toCamera[2]) / span
                        : 0.0f;
        const float t = span > 0.0f ? std::clamp(reach, 0.0f, length) / span : 0.0f;
        float squared = 0.0f;
        for (int i = 0; i < 3; i++) {
            const float off = toCamera[i] - along[i] * t;
            squared += off * off;
        }
        return std::sqrt(squared);
    }

    void __fastcall BuildTraceDetour(void* unused, void* unusedEdx, uint8_t* trace,
                                     const float* camera) {
        float& width = Field<float>(trace, kTraceWidth);
        const float own = width;
        std::copy(camera, camera + 3, g_camera);
        AimingOverhaul::Streak::FollowTexture(Field<uint8_t*>(trace, kTraceTexture));
        if (g_enabled) {
            using namespace AimingOverhaul;
            const float magnification = Aim::ScopeUp() ? Aim::Magnification() : 1.0f;
            width = (std::max)(own, Distance(trace, camera) * kLeastWidth / magnification);
        }
        g_originalBuildTrace(unused, unusedEdx, trace, camera);
        width = own;
    }

    template <class Fn>
    bool Hook(uintptr_t target, Fn detour, Fn* original) {
        return target != 0 && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(target),
                                                         reinterpret_cast<void*>(detour),
                                                         reinterpret_cast<void**>(original));
    }
}

bool AimingOverhaul::Tracers::Install() {
    if (!g_firstPersonTest) {
        FCSE::Logf("tracers: the shot's tracer test was not found, so tracers are the game's own");
        return false;
    }
    const uintptr_t jump = g_firstPersonTest.address() + kJump;
    g_pastTest = jump + kJumpLength;
    g_pastTracer = g_pastTest + *reinterpret_cast<const int32_t*>(jump + 2);
    if (!FCSE::ApiPointer()->MidHook(g_firstPersonTest.get(), &OnFirstPersonTest)) {
        FCSE::Logf("tracers: the shot's tracer test cannot be hooked, so tracers are the game's "
                   "own");
        return false;
    }
    if (!Hook(g_addTrace.address(), &AddTraceDetour, &g_originalAddTrace) ||
        !Hook(g_update.address(), &UpdateDetour, &g_originalUpdate)) {
        FCSE::Logf("tracers: the tracer manager cannot be hooked, so tracers fly as the game "
                   "sends them, and none ricochets");
    }
    if (!Hook(g_buildTrace.address(), &BuildTraceDetour, &g_originalBuildTrace)) {
        FCSE::Logf("tracers: the streak's drawing cannot be hooked, so a far tracer is thin");
    }
    return true;
}

void AimingOverhaul::Tracers::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
