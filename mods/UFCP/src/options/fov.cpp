// Field of view, for the world, the weapon in hand, the sights and vehicles.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/display/fov.ixx, which in
// turn credits FoxAhead's Far Cry 2 Multi Fixer for the original fFOV substitution.
//
// Far Cry 2 renders first person at 75 degrees and offers no way to change it. The value arrives
// through CCameraComponent's fFOV property setter, which takes degrees on the stack and stores
// radians at this+0x70, so substituting the argument is all the base setting takes.
//
// The rest of this file is what makes that safe. A raised field of view is not one number: the
// weapon and arms draw in a second, nearer pass with a projection of their own; ladders and
// cutscenes frame shots that a wide view spoils; the hang glider is a vehicle whose own fFOVAngle
// wins; muzzle particles are split between the two passes and come apart the moment the two
// projections differ; and the map's markers move between passes when the player is in a vehicle.
// Each of those is one more site here, and skipping any of them ships a visible break rather than a
// wider view.
//
// Three principles hold throughout. Fields of view are scaled in tangent space, because that is how
// the engine's own widescreen stretch works and anything else disagrees with it at the edges.
// Clamps narrow only, so a context can pull the view in but never push it out past what the player
// asked for. And every pointer taken out of the engine is page-checked and vtable-checked before
// use, because the pawn that owned it does not survive a load.
#include "fcse_api.h"

#include "engine/memory_probe.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>

namespace {
    constexpr float kPi = 3.14159265f;

    // Degrees, as the settings deliver them. Zero means "leave the engine's own value alone" for
    // the two that have no sensible stock number of their own.
    float g_fieldOfView = 75.0f;
    float g_viewmodelFieldOfView = 75.0f;
    float g_ironsightFieldOfView = 0.0f;
    float g_vehicleFieldOfView = 0.0f;

    // Cutscenes, ladders and gliders break above the stock 75 - reframed shots, and the first
    // person body's own edges coming into view. 45 is the floor the engine itself clamps to.
    constexpr float kFieldOfViewFloor = 45.0f;

    // Hang gliders. Measured before the engine's Hor+ stretch, so this renders as 91.31 on 16:9.
    constexpr float kGliderFieldOfViewMax = 75.0f;

    // Cutscenes and ladders. 59.85 comes back out at 75 after the stretch. One ceiling for both,
    // because a ladder mount enters a scene context and separate ceilings pull the view twice.
    constexpr float kNarrowFieldOfViewMax = 59.85f;

    float GliderCeiling() {
        return std::clamp(g_fieldOfView, kFieldOfViewFloor, kGliderFieldOfViewMax);
    }

    float NarrowCeiling() {
        return std::clamp(g_fieldOfView, kFieldOfViewFloor, kNarrowFieldOfViewMax);
    }

    float DegreesToRadians(float degrees) {
        return degrees * (kPi / 180.0f);
    }

    float RadiansToDegrees(float radians) {
        return radians * (180.0f / kPi);
    }

    // Narrows only. Spelled out rather than std::min, which Windows.h's own macro breaks.
    void NarrowFieldOfView(float* fov, float cap) {
        if (*fov > cap) {
            *fov = cap;
        }
    }

    // Partway to the cap, for a clamp that has to arrive over several frames.
    void BlendFieldOfView(float* fov, float cap, float weight) {
        if (*fov > cap) {
            *fov += (cap - *fov) * weight;
        }
    }

    // CCameraBoneComponent::Update gets the secondary base, +4, so fFOV (+0x70, radians) is at
    // +0x6C.
    constexpr uintptr_t kBoneCameraFieldOfView = 0x6C;

    // The camera state CCameraPawnComponent::Update fills in: world FOV and model FOV, both in
    // radians and both untransformed here.
    constexpr uintptr_t kCameraStateFieldOfView = 0x28;
    constexpr uintptr_t kCameraStateViewmodelFieldOfView = 0x30;

    // Active, on CCameraComponent, reached from the secondary base the update is handed.
    constexpr uintptr_t kCameraActive = 0x0C;

    // The frame delta, the update's first argument.
    constexpr uintptr_t kCameraDeltaTime = 0x08;

    // Roughly the mount animation's length, so the clamp arrives over it instead of snapping.
    constexpr float kNarrowBlendSeconds = 0.35f;

    // A frame delta past this is a load or a hitch, and stepping the blend by it would snap.
    constexpr float kNarrowBlendMaxStep = 0.1f;

    // First-person camera update only, so no atomics. Keyed on the component and never on the
    // pooled camera state, where an owner test only passes on the frames the buffer is reused.
    float g_narrowBlend = 0.0f;
    uintptr_t g_narrowBlendCamera = 0;
    uint32_t g_narrowBlendContext = 0;

    // CPawnBeautifierComponent. The context instance names the situation, the type instance names
    // whose pawn it is, so one classifier covers both.
    constexpr uintptr_t kContextBeautifier = 0x28;
    constexpr uintptr_t kTypeBeautifier = 0x2C;

    enum BeautifierKind : uint32_t {
        kKindOther = 0,
        kKindLadder,
        kKindCutscene,
        kKindPlayer,
    };

    enum NarrowContext : uint32_t {
        kNarrowNone = 0,
        kNarrowLadder,
        kNarrowCutscene,
    };

    // CVehicle's FOV block; the paraglider is named by its own hash.
    constexpr uintptr_t kVehicleName = 0x00;
    constexpr uintptr_t kVehicleFieldOfViewAngle = 0x234;
    constexpr uint32_t kVehicleNameParaglider = 0x7B2D589C;

    // The player's pawn only, so an AI on a ladder cannot narrow the view. Stamped with an expiry
    // rather than latched: quickloading out of a scene destroys the pawn with no transition to
    // clear it.
    std::atomic<uint32_t> g_narrowContext{kNarrowNone};
    std::atomic<uint32_t> g_narrowStamp{0};

    // Long enough to ride out a stutter, short enough that a load releases the clamp unseen.
    constexpr uint32_t kNarrowFreshnessMs = 250;

    // Whether the near pass follows the camera this frame, published by the pawn camera hook.
    std::atomic<bool> g_nearPassFollowsCamera{false};

    // The vehicle channel's weight, sampled once a frame: zero on foot, one in a seat.
    std::atomic<float> g_vehicleFovWeight{0.0f};

    bool NarrowStateIsStale() {
        return GetTickCount() - g_narrowStamp.load(std::memory_order_relaxed) > kNarrowFreshnessMs;
    }

    // Adapter only: this file works in uintptr_t throughout.
    bool IsReadableStruct(uintptr_t address, size_t size) {
        return UFCP::IsReadable(reinterpret_cast<const void*>(address), size);
    }

    struct DuniaClassInfo {
        const char* name;
        uint32_t depth;
    };

    using GetClassInfoFn = const DuniaClassInfo*(__thiscall*)(const void*);

    // Naming an instance: GetClassInfo on primary vtable slot 1, the descriptor's first field being
    // the name literal. Every pointer is range- and page-checked, so a broken convention cannot
    // fault rather than merely failing to match.
    const char* ClassName(uintptr_t object) {
        if (!IsReadableStruct(object, sizeof(void*))) {
            return "";
        }

        void** vtable = *reinterpret_cast<void***>(object);
        if (!UFCP::IsInDunia(vtable) || !IsReadableStruct(reinterpret_cast<uintptr_t>(vtable),
                                                  2 * sizeof(void*)) ||
            !UFCP::IsInDunia(vtable[1])) {
            return "";
        }

        const DuniaClassInfo* info =
            reinterpret_cast<GetClassInfoFn>(vtable[1])(reinterpret_cast<const void*>(object));
        if (!UFCP::IsInDunia(info) || !IsReadableStruct(reinterpret_cast<uintptr_t>(info),
                                                sizeof(DuniaClassInfo))) {
            return "";
        }

        return UFCP::IsInDunia(info->name) ? info->name : "";
    }

    struct BeautifierClass {
        uintptr_t vtable;
        uint32_t kind;
    };

    // Sixteen is more beautifier classes than a pawn cycles through in a session.
    BeautifierClass g_beautifierClasses[16] = {};
    std::mutex g_beautifierClassMutex;

    uint32_t KindOf(uintptr_t instance) {
        if (instance == 0 || !IsReadableStruct(instance, sizeof(void*))) {
            return kKindOther;
        }

        // A freed instance reads garbage here, and refusing to cache it keeps it off a real class.
        const uintptr_t vtable = *reinterpret_cast<uintptr_t*>(instance);
        if (!UFCP::IsInDunia(reinterpret_cast<const void*>(vtable))) {
            return kKindOther;
        }

        std::scoped_lock lock(g_beautifierClassMutex);

        for (const BeautifierClass& entry : g_beautifierClasses) {
            if (entry.vtable == vtable) {
                return entry.kind;
            }
        }

        const char* name = ClassName(instance);

        uint32_t kind = kKindOther;
        if (std::strcmp(name, "CPawnBeautifierLadder") == 0) {
            kind = kKindLadder;
        } else if (std::strcmp(name, "CPawnBeautifierDominoPlayer") == 0 ||
                   std::strcmp(name, "CPawnBeautifierCinematicFirst") == 0 ||
                   std::strcmp(name, "CPawnBeautifierFirstNoControl") == 0) {
            kind = kKindCutscene;
        } else if (std::strcmp(name, "CPawnBeautifierPlayer") == 0) {
            kind = kKindPlayer;
        }

        for (BeautifierClass& entry : g_beautifierClasses) {
            if (entry.vtable == 0) {
                entry.vtable = vtable;
                entry.kind = kind;
                break;
            }
        }

        return kind;
    }

    // Scaled in tangent space, to match the game's own widescreen scaling.
    float ScaleFov(float fovRadians, float scale) {
        if (scale == 1.0f) {
            return fovRadians;
        }

        return 2.0f * std::atan(std::tan(fovRadians * 0.5f) * scale);
    }

    // The engine's own widescreen stretch factor.
    constexpr float kWidescreenStretch = 0.75f;

    // tan(viewmodel FOV / 2), kept because the near pass wants it every frame and the setting moves
    // at human speed.
    std::atomic<float> g_viewmodelHalfTangent{1.0f};

    // How far the sights are up, sampled once a frame: zero down, one fully raised.
    std::atomic<float> g_ironsightBlend{0.0f};

    // How far the clamp has pulled the world in, in tangent space, and the ceiling it heads for.
    // One is zero exactly when the other is.
    std::atomic<float> g_narrowTangentScale{1.0f};
    std::atomic<float> g_narrowViewmodelCeiling{0.0f};

    void ClearNarrowViewmodel() {
        g_narrowTangentScale.store(1.0f, std::memory_order_relaxed);
        g_narrowViewmodelCeiling.store(0.0f, std::memory_order_relaxed);
    }

    float ViewmodelScaleFor(float fovRadians, float aspect) {
        const float cameraTan = std::tan(fovRadians * 0.5f);
        if (cameraTan <= 0.0f) {
            return 1.0f;
        }

        float viewmodelTan = g_viewmodelHalfTangent.load(std::memory_order_relaxed) *
                             g_narrowTangentScale.load(std::memory_order_relaxed) *
                             kWidescreenStretch * aspect;

        // The camera state is clamped before the widescreen stretch, so the ceiling takes it here.
        const float ceiling = g_narrowViewmodelCeiling.load(std::memory_order_relaxed);
        if (ceiling > 0.0f) {
            const float ceilingTan = std::tan(ceiling * 0.5f) * kWidescreenStretch * aspect;
            if (viewmodelTan > ceilingTan) {
                viewmodelTan = ceilingTan;
            }
        }

        const float scale = viewmodelTan / cameraTan;
        if (scale <= 1.0f) {
            return scale;
        }

        // A viewmodel FOV above the world's is legitimate, so there is no flat ceiling of one -
        // except with the sights up, where widening the near pass back out holds the gun off the
        // eye.
        return g_ironsightBlend.load(std::memory_order_relaxed) > 0.0f ? 1.0f : scale;
    }

    // Magnified optics use the same property, and are left alone so scopes keep their zoom.
    constexpr float kMagnifiedOpticCutoff = 40.0f;

    // The weapon property object's ironsight FOV, in radians unlike the vehicle's degrees.
    constexpr uintptr_t kWeaponIronsightFieldOfView = 0xE4;

    // Past the FLD and the FSTP that copy it into the channel, six bytes and three.
    constexpr ptrdiff_t kIronsightFovCopied = 9;

    // The first-person camera's own FOV, on the primary base. Not 0x6C, the model's FOV: writing
    // that ties the world's field of view to the viewmodel's.
    constexpr uintptr_t kPawnCameraFieldOfView = 0x70;

    // The pawn's FOV channels, blended from a CPawnFOV rather than read where they are set. Two
    // 0x1C records: ironsight at 0x08, base at 0x24.
    constexpr uintptr_t kPawnFovIronsightValue = 0x1C;
    constexpr uintptr_t kPawnFovIronsightWeight = 0x18;
    constexpr uintptr_t kPawnFovBaseValue = 0x38;
    constexpr uintptr_t kPawnFovBaseWeight = 0x34;
    constexpr uintptr_t kPawnFovBaseArmed = 0x29;
    constexpr size_t kPawnFovSize = 0x60;

    // No path to the struct from a global, so it is remembered where the engine hands it over, and
    // vtable-checked before reuse: the owning pawn does not survive a load.
    std::atomic<uintptr_t> g_pawnFieldOfView{0};
    std::atomic<uintptr_t> g_pawnFieldOfViewVTable{0};

    void RememberPawnFieldOfView(uintptr_t block) {
        // Called once a frame from the blend, so a repeat pointer costs a compare rather than a
        // page query.
        if (block == 0 || block == g_pawnFieldOfView.load(std::memory_order_relaxed)) {
            return;
        }

        if (!IsReadableStruct(block, kPawnFovSize)) {
            return;
        }

        g_pawnFieldOfView.store(block, std::memory_order_relaxed);

        // Learned from the first struct handed over, so no pattern is spent finding the vtable.
        if (g_pawnFieldOfViewVTable.load(std::memory_order_relaxed) == 0) {
            g_pawnFieldOfViewVTable.store(*reinterpret_cast<uintptr_t*>(block),
                                          std::memory_order_relaxed);
        }
    }

    uintptr_t LivePawnFieldOfView() {
        const uintptr_t block = g_pawnFieldOfView.load(std::memory_order_relaxed);
        const uintptr_t vtable = g_pawnFieldOfViewVTable.load(std::memory_order_relaxed);

        if (block == 0 || vtable == 0 || !IsReadableStruct(block, kPawnFovSize)) {
            return 0;
        }

        return *reinterpret_cast<uintptr_t*>(block) == vtable ? block : 0;
    }

    // Whether the player's own weapon is a magnified optic. An already-pushed value reads back as a
    // scope, so the push cannot judge it; the setup hook decides and leaves the answer here.
    std::atomic<bool> g_ironsightIsMagnifiedOptic{false};

    // The seat FOV push is the only place the paraglider is identified, so the answer is kept.
    std::atomic<bool> g_seatIsParaglider{false};

    // Re-pushes both channels from the settings, so a change reaches the pawn without waiting for
    // the next weapon setup or seat. Armed flags are only set where one already was, so a player on
    // foot is never handed a vehicle's field of view.
    void PushPawnFieldOfView() {
        const uintptr_t block = LivePawnFieldOfView();
        if (block == 0) {
            return;
        }

        if (g_ironsightFieldOfView > 0.0f &&
            !g_ironsightIsMagnifiedOptic.load(std::memory_order_relaxed)) {
            *reinterpret_cast<float*>(block + kPawnFovIronsightValue) =
                DegreesToRadians(g_ironsightFieldOfView);
        }

        if (*reinterpret_cast<uint8_t*>(block + kPawnFovBaseArmed) == 0) {
            return;
        }

        // The paraglider takes the glider ceiling and never the vehicle setting, because this push
        // lands after the seat clamp. Written outright, since narrowing could not raise it back.
        if (g_seatIsParaglider.load(std::memory_order_relaxed) &&
            g_vehicleFovWeight.load(std::memory_order_relaxed) > 0.0f) {
            *reinterpret_cast<float*>(block + kPawnFovBaseValue) =
                DegreesToRadians(GliderCeiling());
            *reinterpret_cast<uint8_t*>(block + kPawnFovBaseArmed) = 1;
            return;
        }

        if (g_vehicleFieldOfView > 0.0f) {
            *reinterpret_cast<float*>(block + kPawnFovBaseValue) =
                DegreesToRadians(g_vehicleFieldOfView);
            *reinterpret_cast<uint8_t*>(block + kPawnFovBaseArmed) = 1;
        }
    }

    // Muzzle particles: the weapon draws in the near pass and its flash and smoke in the world one,
    // so the two come apart once the fields of view differ. The mask is read from the owner - one
    // on the weapon, zero on every AI.
    constexpr uintptr_t kGraphicFirstPersonMask = 0x12C;

    using SetFirstPersonLayerMaskFn = void(__fastcall*)(void* self, void* unused, uint32_t mask);
    using PushFirstPersonLayerFn = void(__fastcall*)(uintptr_t component);

    SetFirstPersonLayerMaskFn g_setFirstPersonLayerMask = nullptr;
    PushFirstPersonLayerFn g_pushFirstPersonLayer = nullptr;

    // EAX is the resolved particle instance, one instruction before the Start that consumes it;
    // EBX is the owner's CGraphicComponent.
    void MuzzleFirstPersonMaskHandler(FCSE_MidHookContext* ctx) {
        if (ctx->eax == 0) {
            return;
        }

        const uintptr_t graphic = ctx->ebx;
        if (!IsReadableStruct(graphic, kGraphicFirstPersonMask + sizeof(uint32_t))) {
            return;
        }

        const uint32_t mask = *reinterpret_cast<uint32_t*>(graphic + kGraphicFirstPersonMask);

        // Zero is what the emitters already carry, so third person and AI are left alone.
        if (mask == 0) {
            return;
        }

        g_setFirstPersonLayerMask(reinterpret_cast<void*>(ctx->eax), nullptr, mask);
    }

    // The machete blade mid-swing: the layer comes off the weapon while the blade still draws at
    // the eye. Only timing tells a swing from a swap, so the clear is withheld until a show, a
    // scripted pose, or this deadline.
    constexpr float kSwingMaxSeconds = 1.6f;

    // selWeaponClass; class zero is every HandToHand archetype. Not sufficient alone, but it keeps
    // out every hide made with a gun in hand.
    constexpr uintptr_t kWeaponClass = 0x40;
    constexpr uint32_t kWeaponClassMelee = 0;

    std::atomic<bool> g_meleeInHand{false};

    void RememberMeleeWeapon(uint32_t weaponClass) {
        g_meleeInHand.store(weaponClass == kWeaponClassMelee, std::memory_order_relaxed);
    }

    std::atomic<uintptr_t> g_layerWithheldFrom{0};
    std::atomic<uint32_t> g_layerWithheldBits{0};

    // Gameplay seconds, stepped by the camera update's delta: on GetTickCount a pause aged the
    // hold.
    std::atomic<float> g_layerWithheldFor{0.0f};

    // The player's first-person set, kept in one pass for a scripted scene, which hides exactly one
    // component and leaves the rest winning on near-pass depth.
    constexpr size_t kFirstPersonSetMax = 32;

    struct FirstPersonComponent {
        uintptr_t component;

        // The vtable from census time, when the component was certainly live. Compared, never
        // called.
        uintptr_t vtable;

        uint32_t suppressed;

        // Set when the engine hid or showed this component during the scene; handing our bits back
        // then would write over a decision it made after the copy was taken.
        bool engineTouched;
    };

    FirstPersonComponent g_firstPersonSet[kFirstPersonSetMax] = {};
    std::mutex g_firstPersonSetMutex;
    std::atomic<bool> g_firstPersonSetSuppressed{false};

    // Our own pushes re-enter the census hook, which would take the mutex already held.
    thread_local bool g_writingLayer = false;

    // A recorded pointer outlives its entity, and page checks still pass on the reused address. The
    // validator must not execute anything, so the census vtable is compared rather than named.
    bool IsLiveEntry(const FirstPersonComponent& entry) {
        if (entry.component == 0 || entry.vtable == 0) {
            return false;
        }

        if (!IsReadableStruct(entry.component, kGraphicFirstPersonMask + sizeof(uint32_t))) {
            return false;
        }

        return *reinterpret_cast<uintptr_t*>(entry.component) == entry.vtable;
    }

    void RememberFirstPersonComponent(FCSE_MidHookContext* ctx) {
        if (g_writingLayer) {
            return;
        }

        // This entry is hot, so it is read directly rather than page-queried per call.
        const uintptr_t component = ctx->ecx;
        if (component == 0) {
            return;
        }

        const uintptr_t vtable = *reinterpret_cast<uintptr_t*>(component);
        if (!UFCP::IsInDunia(reinterpret_cast<const void*>(vtable))) {
            return;
        }

        // Only the ones carrying a mask: an AI's components push a zero through the same call.
        if (*reinterpret_cast<uint32_t*>(component + kGraphicFirstPersonMask) == 0) {
            return;
        }

        std::scoped_lock lock(g_firstPersonSetMutex);

        for (const FirstPersonComponent& entry : g_firstPersonSet) {
            if (entry.component == component) {
                return;
            }
        }

        for (FirstPersonComponent& entry : g_firstPersonSet) {
            if (entry.component == 0) {
                entry.component = component;
                entry.vtable = vtable;
                entry.suppressed = 0;
                entry.engineTouched = false;
                return;
            }
        }
    }

    // Whether the engine has already taken one of the set into the world pass. With the set whole,
    // the right number of writes is none.
    bool FirstPersonSetIsSplit() {
        bool anyHidden = false;
        bool anyShown = false;

        for (const FirstPersonComponent& entry : g_firstPersonSet) {
            if (!IsLiveEntry(entry)) {
                continue;
            }

            if (*reinterpret_cast<uint32_t*>(entry.component + kGraphicFirstPersonMask) == 0) {
                anyHidden = true;
            } else {
                anyShown = true;
            }
        }

        return anyHidden && anyShown;
    }

    void SuppressFirstPersonSet() {
        if (g_firstPersonSetSuppressed.load(std::memory_order_relaxed)) {
            return;
        }

        std::scoped_lock lock(g_firstPersonSetMutex);

        if (!FirstPersonSetIsSplit()) {
            return;
        }

        g_firstPersonSetSuppressed.store(true, std::memory_order_relaxed);
        g_writingLayer = true;

        for (FirstPersonComponent& entry : g_firstPersonSet) {
            if (entry.component == 0) {
                continue;
            }

            // Freed or reused between scenes: dropped rather than written.
            if (!IsLiveEntry(entry)) {
                entry.component = 0;
                entry.vtable = 0;
                entry.suppressed = 0;
                continue;
            }

            uint32_t* mask =
                reinterpret_cast<uint32_t*>(entry.component + kGraphicFirstPersonMask);

            // Already in the world pass: the one the engine hid, with nothing to save.
            if (*mask == 0) {
                continue;
            }

            entry.suppressed = *mask;
            entry.engineTouched = false;
            *mask = 0;

            if (g_pushFirstPersonLayer != nullptr) {
                g_pushFirstPersonLayer(entry.component);
            }
        }

        g_writingLayer = false;
    }

    void RestoreFirstPersonSet() {
        if (!g_firstPersonSetSuppressed.exchange(false, std::memory_order_relaxed)) {
            return;
        }

        std::scoped_lock lock(g_firstPersonSetMutex);

        g_writingLayer = true;

        for (FirstPersonComponent& entry : g_firstPersonSet) {
            const uint32_t suppressed = entry.suppressed;
            const bool touched = entry.engineTouched;

            entry.suppressed = 0;
            entry.engineTouched = false;

            if (entry.component == 0 || suppressed == 0 || touched) {
                continue;
            }

            if (!IsLiveEntry(entry)) {
                entry.component = 0;
                entry.vtable = 0;
                continue;
            }

            *reinterpret_cast<uint32_t*>(entry.component + kGraphicFirstPersonMask) |= suppressed;

            if (g_pushFirstPersonLayer != nullptr) {
                g_pushFirstPersonLayer(entry.component);
            }
        }

        g_writingLayer = false;
    }

    void ForgetFirstPersonSet() {
        std::scoped_lock lock(g_firstPersonSetMutex);

        for (FirstPersonComponent& entry : g_firstPersonSet) {
            entry.component = 0;
            entry.vtable = 0;
            entry.suppressed = 0;
            entry.engineTouched = false;
        }
    }

    // Once a frame on the gameplay thread, keyed on the context rather than the hide, because
    // either can arrive first.
    void UpdateFirstPersonSetForScene() {
        // A stale narrow state is a load or a teardown, so the table is dropped and the census
        // fills it again.
        if (NarrowStateIsStale()) {
            RestoreFirstPersonSet();
            ForgetFirstPersonSet();
            return;
        }

        // A seat is the engine's own split, already answered by the near pass fade, and suppressing
        // there fights it. Boats and gliders reach a scene context while mounting.
        const bool scene = g_narrowContext.load(std::memory_order_relaxed) == kNarrowCutscene &&
                           g_vehicleFovWeight.load(std::memory_order_relaxed) <= 0.0f;

        if (scene) {
            SuppressFirstPersonSet();
        } else {
            RestoreFirstPersonSet();
        }
    }

    // Dropped rather than written when the component no longer carries the withheld bits.
    void ReleaseWithheldLayer() {
        const uintptr_t component = g_layerWithheldFrom.exchange(0, std::memory_order_relaxed);
        const uint32_t bits = g_layerWithheldBits.load(std::memory_order_relaxed);

        if (component == 0 || bits == 0) {
            return;
        }

        if (!IsReadableStruct(component, kGraphicFirstPersonMask + sizeof(uint32_t))) {
            return;
        }

        uint32_t* mask = reinterpret_cast<uint32_t*>(component + kGraphicFirstPersonMask);
        if ((*mask & bits) != bits) {
            return;
        }

        *mask &= ~bits;

        if (g_pushFirstPersonLayer != nullptr) {
            g_pushFirstPersonLayer(component);
        }
    }

    // A seat, a ladder or a cutscene, each taking the weapon out of the player's hands through the
    // call a swing uses.
    bool InScriptedPose() {
        if (g_vehicleFovWeight.load(std::memory_order_relaxed) > 0.0f) {
            return true;
        }

        if (NarrowStateIsStale()) {
            return false;
        }

        return g_narrowContext.load(std::memory_order_relaxed) != kNarrowNone;
    }

    // The deadline, on the gameplay thread the delta comes from. A swing is back in about 1.3
    // seconds, so this only fires for a hide whose show never came.
    void StepWithheldLayer(float delta) {
        if (g_layerWithheldFrom.load(std::memory_order_acquire) == 0) {
            return;
        }

        const float heldFor = g_layerWithheldFor.load(std::memory_order_relaxed) + delta;
        g_layerWithheldFor.store(heldFor, std::memory_order_relaxed);

        if (heldFor > kSwingMaxSeconds) {
            ReleaseWithheldLayer();
        }
    }

    void ReleaseWithheldLayerIfStale() {
        if (g_layerWithheldFrom.load(std::memory_order_acquire) == 0) {
            return;
        }

        if (InScriptedPose()) {
            ReleaseWithheldLayer();
        }
    }

    // CGraphicComponent's enable/disable form, __thiscall(mask, enable).
    void HoldFirstPersonLayerHandler(FCSE_MidHookContext* ctx) {
        const uintptr_t component = ctx->ecx;
        if (component == 0) {
            return;
        }

        uint32_t* mask = reinterpret_cast<uint32_t*>(ctx->esp + 4);
        const bool enable = *reinterpret_cast<uint8_t*>(ctx->esp + 8) != 0;

        // While suppressed the engine's own change outranks our copy: mark it, never hand it back.
        if (g_firstPersonSetSuppressed.load(std::memory_order_relaxed)) {
            std::scoped_lock lock(g_firstPersonSetMutex);

            for (FirstPersonComponent& entry : g_firstPersonSet) {
                if (entry.component == component) {
                    entry.engineTouched = true;
                    break;
                }
            }
        }

        if (enable) {
            // The same component coming back is the swing ending; it already holds the value.
            if (g_layerWithheldFrom.load(std::memory_order_relaxed) == component) {
                g_layerWithheldFrom.store(0, std::memory_order_relaxed);
                return;
            }

            // A different component means the hide was a swap.
            ReleaseWithheldLayer();
            return;
        }

        if (!g_meleeInHand.load(std::memory_order_relaxed) || InScriptedPose()) {
            return;
        }

        if (!IsReadableStruct(component, kGraphicFirstPersonMask + sizeof(uint32_t))) {
            return;
        }

        // Only a component already carrying the layer: an AI's machete never has it.
        const uint32_t withheld =
            *reinterpret_cast<uint32_t*>(component + kGraphicFirstPersonMask) & *mask;
        if (withheld == 0) {
            return;
        }

        // A second hide with no show between them means the first was a swap as well.
        if (g_layerWithheldFrom.load(std::memory_order_relaxed) != 0) {
            ReleaseWithheldLayer();
        }

        // Elapsed and bits before the component: the render thread keys on the component, so
        // publishing it last lets a stale elapsed time release the swing on its first frame.
        g_layerWithheldFor.store(0.0f, std::memory_order_relaxed);
        g_layerWithheldBits.store(withheld, std::memory_order_relaxed);
        g_layerWithheldFrom.store(component, std::memory_order_release);

        *mask = 0;
    }

    // Map markers are 3D entities on CCompassObjectives. In a vehicle the map moves to the world
    // pass while the markers stay in the near pass, which viewmodel scaling then misaligns.
    constexpr uintptr_t kCompassInVehicle = 0x28;

    std::atomic<bool> g_markersInVehicle{false};
    std::atomic<uint32_t> g_markersStamp{0};

    // Marker placement stops when the map closes, so the flag is only valid briefly after one.
    constexpr uint32_t kMarkerFreshnessMs = 250;

    bool MapIsInVehicle() {
        if (!g_markersInVehicle.load(std::memory_order_relaxed)) {
            return false;
        }

        return GetTickCount() - g_markersStamp.load(std::memory_order_relaxed) <=
               kMarkerFreshnessMs;
    }

    // The near pass copies the world's constant block and rewrites only the matrix, so a different
    // field of view draws against the wrong frustum and slides. The projection hook and those
    // constants have to agree, so the answer is worked out once here.
    float NearPassFieldOfView(float cameraFov, float aspect) {
        if (MapIsInVehicle() || g_nearPassFollowsCamera.load(std::memory_order_relaxed)) {
            return cameraFov;
        }

        // Not named `near`: windef.h still defines that as a macro.
        const float nearFov = ScaleFov(cameraFov, ViewmodelScaleFor(cameraFov, aspect));

        // In a seat the near pass follows the camera, or the hands are not where the wheel is.
        // Faded over the seat's own transition time rather than switched, in tangent space.
        const float seat = std::clamp(g_vehicleFovWeight.load(std::memory_order_relaxed), 0.0f, 1.0f);
        if (seat <= 0.0f) {
            return nearFov;
        }

        // Seated, the camera's own number rather than the arithmetic that arrives at it: the
        // tan/atan round trip lands a few ulps away.
        if (seat >= 1.0f) {
            return cameraFov;
        }

        const float nearTan = std::tan(nearFov * 0.5f);
        const float cameraTan = std::tan(cameraFov * 0.5f);

        return 2.0f * std::atan(nearTan + (cameraTan - nearTan) * seat);
    }

    // Shared by the two inlined copies of the seat FOV push. The paraglider is clamped to the
    // glider ceiling; every other vehicle takes the setting outright.
    void ApplySeatFieldOfView(uintptr_t vehicle) {
        const bool paraglider =
            *reinterpret_cast<uint32_t*>(vehicle + kVehicleName) == kVehicleNameParaglider;

        g_seatIsParaglider.store(paraglider, std::memory_order_relaxed);

        if (paraglider) {
            NarrowFieldOfView(reinterpret_cast<float*>(vehicle + kVehicleFieldOfViewAngle),
                              GliderCeiling());
            return;
        }

        if (g_vehicleFieldOfView > 0.0f) {
            *reinterpret_cast<float*>(vehicle + kVehicleFieldOfViewAngle) = g_vehicleFieldOfView;
        }
    }

    // Both seat hooks: EAX the pawn's FOV channels, ESI the vehicle.
    void SeatFieldOfViewHandler(FCSE_MidHookContext* ctx) {
        RememberPawnFieldOfView(ctx->eax);
        ApplySeatFieldOfView(ctx->esi);
    }

    void FieldOfViewSetterHandler(FCSE_MidHookContext* ctx) {
        *reinterpret_cast<float*>(ctx->esp + 4) = g_fieldOfView;
    }

    // The near pass for the weapon and arms, which has a projection of its own. ESI is the camera.
    void ViewmodelHandler(FCSE_MidHookContext* ctx) {
        ReleaseWithheldLayerIfStale();

        float* fov = reinterpret_cast<float*>(ctx->esp);
        *fov = NearPassFieldOfView(*fov, *reinterpret_cast<float*>(ctx->esi + 0x18));
    }

    void CompassMarkerHandler(FCSE_MidHookContext* ctx) {
        g_markersInVehicle.store(*reinterpret_cast<bool*>(ctx->ecx + kCompassInVehicle),
                                 std::memory_order_relaxed);
        g_markersStamp.store(GetTickCount(), std::memory_order_relaxed);
    }

    // The Widescreen option's blend weight can stick at zero after a toggle, leaving the field of
    // view unwidened. The branch is forced and the weight pinned; it is recomputed every frame.
    void WidescreenHandler(FCSE_MidHookContext* ctx) {
        constexpr uintptr_t kZeroFlag = 0x40;

        ctx->eflags &= ~kZeroFlag;
        *reinterpret_cast<float*>(ctx->edi + 0x58) = 1.0f;
    }

    // Cutscenes. CCameraBoneComponent::Update stores fFOV into both the world and model fields, so
    // rewriting it ahead of the first FLD covers both.
    void CinematicHandler(FCSE_MidHookContext* ctx) {
        NarrowFieldOfView(reinterpret_cast<float*>(ctx->edi + kBoneCameraFieldOfView),
                          DegreesToRadians(NarrowCeiling()));
    }

    // Ladders and first-person scenes carry no field of view to intercept, so the beautifier's
    // context is read at its update's entry, one frame old.
    void BeautifierHandler(FCSE_MidHookContext* ctx) {
        const uintptr_t component = ctx->ecx - 4;

        // Every AI updates through here, so the type half is checked first.
        if (KindOf(*reinterpret_cast<uintptr_t*>(component + kTypeBeautifier)) != kKindPlayer) {
            return;
        }

        uint32_t context = kNarrowNone;
        switch (KindOf(*reinterpret_cast<uintptr_t*>(component + kContextBeautifier))) {
        case kKindLadder:
            context = kNarrowLadder;
            break;
        case kKindCutscene:
            context = kNarrowCutscene;
            break;
        default:
            break;
        }

        g_narrowContext.store(context, std::memory_order_relaxed);
        g_narrowStamp.store(GetTickCount(), std::memory_order_relaxed);
    }

    // At the head of the frame's FOV blend: the camera's own field is rebuilt every frame and the
    // setter only seeds it. ESI is the camera, EAX the CPawnFOV.
    void CameraBlendHandler(FCSE_MidHookContext* ctx) {
        // Before the MOVSS this stands on, so the blend uses it on this frame.
        *reinterpret_cast<float*>(ctx->esi + kPawnCameraFieldOfView) =
            DegreesToRadians(g_fieldOfView);

        RememberPawnFieldOfView(ctx->eax);
        g_ironsightBlend.store(*reinterpret_cast<float*>(ctx->eax + kPawnFovIronsightWeight),
                               std::memory_order_relaxed);
        g_vehicleFovWeight.store(*reinterpret_cast<float*>(ctx->eax + kPawnFovBaseWeight),
                                 std::memory_order_relaxed);

        // The swing deadline, and the scene's layer bookkeeping: this is the one hook running every
        // frame on the gameplay thread, and the set's first-use allocation faults from the near
        // pass hook.
        ReleaseWithheldLayerIfStale();
        UpdateFirstPersonSetForScene();
    }

    // CCameraPawnComponent::Update for first person, both stores landed and before the widescreen
    // transform. The blend is tied to the camera it raised.
    void NarrowHandler(FCSE_MidHookContext* ctx) {
        // Only the camera actually driving the view; an inactive one still updates.
        if (*reinterpret_cast<uint8_t*>(ctx->edi + kCameraActive) == 0) {
            return;
        }

        // On the update's own delta, so a paused game does not age the hold.
        const float frame = *reinterpret_cast<float*>(ctx->ebp + kCameraDeltaTime);
        StepWithheldLayer(std::clamp(frame, 0.0f, kNarrowBlendMaxStep));

        // The pawn that owned the state is gone, so the clamp is dropped outright.
        if (NarrowStateIsStale()) {
            g_narrowBlend = 0.0f;
            g_narrowBlendCamera = 0;
            g_narrowBlendContext = kNarrowNone;
            ClearNarrowViewmodel();
            g_nearPassFollowsCamera.store(false, std::memory_order_relaxed);
            return;
        }

        const uint32_t context = g_narrowContext.load(std::memory_order_relaxed);
        const bool narrow = context != kNarrowNone;

        // A different active camera starts again rather than carrying a weight across views, and
        // never holds a pointer that is not coming back.
        if (g_narrowBlendCamera != ctx->edi) {
            g_narrowBlendCamera = ctx->edi;
            g_narrowBlend = 0.0f;
        }

        // So the near pass follows the camera on exactly the frames the world is clamped. The blend
        // out counts, or the viewmodel jumps out of every scene.
        if (context == kNarrowCutscene) {
            g_narrowBlendContext = kNarrowCutscene;
        }

        g_nearPassFollowsCamera.store(context == kNarrowCutscene ||
                                          (g_narrowBlend > 0.0f &&
                                           g_narrowBlendContext == kNarrowCutscene),
                                      std::memory_order_relaxed);

        if (!narrow && g_narrowBlend <= 0.0f) {
            ClearNarrowViewmodel();
            return;
        }

        const float step = std::clamp(frame, 0.0f, kNarrowBlendMaxStep) / kNarrowBlendSeconds;

        g_narrowBlend = std::clamp(g_narrowBlend + (narrow ? step : -step), 0.0f, 1.0f);
        if (g_narrowBlend <= 0.0f) {
            g_narrowBlendContext = kNarrowNone;
            ClearNarrowViewmodel();
            g_nearPassFollowsCamera.store(false, std::memory_order_relaxed);
            return;
        }

        // Smoothstep, so the move leaves and arrives at rest. Both contexts take the same ceiling;
        // the context still tells them apart.
        const float weight = g_narrowBlend * g_narrowBlend * (3.0f - 2.0f * g_narrowBlend);
        const float fov = DegreesToRadians(NarrowCeiling());

        float* worldFov = reinterpret_cast<float*>(ctx->esi + kCameraStateFieldOfView);
        const float before = *worldFov;

        BlendFieldOfView(worldFov, fov, weight);
        BlendFieldOfView(reinterpret_cast<float*>(ctx->esi + kCameraStateViewmodelFieldOfView), fov,
                         weight);

        // Off the world FOV rather than the blend, so an unmoved frame carries a scale of one.
        const float beforeTan = std::tan(before * 0.5f);
        g_narrowTangentScale.store(
            beforeTan > 0.0f ? std::tan(*worldFov * 0.5f) / beforeTan : 1.0f,
            std::memory_order_relaxed);

        g_narrowViewmodelCeiling.store(fov, std::memory_order_relaxed);
    }

    // Ironsight FOV, taken where the weapon hands it to the pawn: the property is copied into the
    // channel, both in radians. Never the weapon's own field, which breaks every other holder.
    void IronsightHandler(FCSE_MidHookContext* ctx) {
        // Every pawn arms itself through here, so the player's channel is picked out first. An
        // unset handle is accepted, the session's first setup being his.
        const uintptr_t player = g_pawnFieldOfView.load(std::memory_order_relaxed);
        if (player != 0 && ctx->edi != player) {
            return;
        }

        // EDI is the pawn's FOV channels, taken whether or not the setting is in use: it is the
        // handle a later change needs.
        RememberPawnFieldOfView(ctx->edi);
        RememberMeleeWeapon(*reinterpret_cast<uint32_t*>(ctx->eax + kWeaponClass));

        // In degrees for the comparison, both ends of this path being radians. Decided either way,
        // since the push needs the answer for whatever is in hand.
        const float weapon = *reinterpret_cast<const float*>(ctx->eax + kWeaponIronsightFieldOfView);
        const bool optic = RadiansToDegrees(weapon) < kMagnifiedOpticCutoff;

        g_ironsightIsMagnifiedOptic.store(optic, std::memory_order_relaxed);

        if (g_ironsightFieldOfView <= 0.0f || optic) {
            return;
        }

        *reinterpret_cast<float*>(ctx->edi + kPawnFovIronsightValue) =
            DegreesToRadians(g_ironsightFieldOfView);
    }

    FCSE::Relocation<uint8_t*> g_setter{
        FCSE::Pattern("F3 0F 10 44 24 04 F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 41 70 C2 04 00")};
    FCSE::Relocation<uint8_t*> g_viewmodel{
        FCSE::Pattern("D9 86 28 02 00 00 D9 1C 24 E8 ?? ?? ?? ?? D9 45 14")};
    FCSE::Relocation<uint8_t*> g_firstPersonMask{
        FCSE::Pattern("8B BF 2C 01 00 00 57 8D 4C 24 20 E8 ?? ?? ?? ?? 8B C8 E8")};
    FCSE::Relocation<uint8_t*> g_muzzleFirst{FCSE::Pattern(
        "84 C0 75 59 8D 54 24 20 52 8D 4C 24 1C E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 8D 4C 24 18 "
        "E8 ?? ?? ?? ?? 8B C8 E8")};
    FCSE::Relocation<uint8_t*> g_muzzleSecond{FCSE::Pattern(
        "84 C0 75 59 8D 44 24 10 50 8D 4C 24 10 E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 8D 4C 24 0C "
        "E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 8B 97 A0 00 00 00")};
    FCSE::Relocation<uint8_t*> g_layerPush{
        FCSE::Pattern("56 8B F1 83 BE C0 01 00 00 00 57 75 25 8B 46 08")};
    FCSE::Relocation<uint8_t*> g_layerToggle{
        FCSE::Pattern("80 7C 24 08 00 56 8B F1 74 1E 8B 8E 30 01 00 00")};
    FCSE::Relocation<uint8_t*> g_compassMarker{
        FCSE::Pattern("55 8B EC 83 E4 F0 81 EC 84 00 00 00 53 56 57 8B 7D 10 8B 77 0C 8B D9")};
    FCSE::Relocation<uint8_t*> g_widescreen{FCSE::Pattern(
        "8B 0D ?? ?? ?? ?? 83 79 40 00 0F 84 C4 00 00 00 E8 ?? ?? ?? ?? F3 0F 10 40 10")};
    FCSE::Relocation<uint8_t*> g_cinematic{
        FCSE::Pattern("D9 47 6C 8B 54 24 40 D9 5E 28 D9 47 6C 52 D9 5E 30")};
    FCSE::Relocation<uint8_t*> g_beautifier{FCSE::Pattern(
        "55 56 8B E9 57 8B 7D 04 8B 77 0C 83 47 08 01 8B CE E8 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 00 "
        "75 07")};
    FCSE::Relocation<uint8_t*> g_cameraBlend{
        FCSE::Pattern("F3 0F 10 4E 70 F3 0F 10 47 14 0F 57 D2 F3 0F 5C C1 F3 0F 59 47 10")};
    FCSE::Relocation<uint8_t*> g_pawnCamera{
        FCSE::Pattern("D9 47 6C 8B 4C 24 3C D9 5E 30 8A 9E 80 00 00 00")};
    FCSE::Relocation<uint8_t*> g_gliderFov{FCSE::Pattern(
        "8B CB E8 ?? ?? ?? ?? D9 86 30 02 00 00 D9 58 2C 8B 8E 54 02 00 00 89 48 3C F3 0F 10 86 "
        "34 02 00 00 F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 40 38 C6 40 29 01")};
    FCSE::Relocation<uint8_t*> g_gliderSeatFov{FCSE::Pattern(
        "8B 4C 24 08 74 ?? E8 ?? ?? ?? ?? D9 86 30 02 00 00 D9 58 2C 8B 8E 54 02 00 00 89 48 3C "
        "F3 0F 10 86 34 02 00 00 F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 40 38 C6 40 29 01")};
    FCSE::Relocation<uint8_t*> g_ironsightFov{FCSE::Pattern("D9 80 E4 00 00 00 D9 5F 1C")};

    // Offsets into the matches above, each naming the instruction the hook stands on.
    constexpr ptrdiff_t kViewmodelFovPushed = 9;
    constexpr ptrdiff_t kFirstPersonMaskCall = 0x12;
    constexpr ptrdiff_t kMuzzleInstanceReady = 0x22;
    constexpr ptrdiff_t kWidescreenBranch = 10;
    constexpr ptrdiff_t kPawnCameraStored = 10;
    constexpr ptrdiff_t kGliderSeat = 0x19;
    constexpr ptrdiff_t kGliderSeatAlternate = 0x1D;

    // Resolves a call's target from its displacement.
    uint8_t* CallTarget(uint8_t* call) {
        return call + 5 + *reinterpret_cast<int32_t*>(call + 1);
    }
}

void InstallFovHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_setter) {
        api->Log("fov: the camera's fFOV setter was not found in this build - field of view cannot "
                 "be changed");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_setter.address()), &FieldOfViewSetterHandler);

    if (g_viewmodel) {
        api->MidHook(reinterpret_cast<void*>(g_viewmodel.address() + kViewmodelFovPushed),
                     &ViewmodelHandler);
    }

    // Off the spawner's own call, the target having no prologue to anchor on.
    if (g_firstPersonMask) {
        g_setFirstPersonLayerMask = reinterpret_cast<SetFirstPersonLayerMaskFn>(
            CallTarget(reinterpret_cast<uint8_t*>(g_firstPersonMask.address() +
                                                  kFirstPersonMaskCall)));
    }

    // The two weapon-effect spawners, differing only in stack layout.
    if (g_setFirstPersonLayerMask != nullptr) {
        if (g_muzzleFirst) {
            api->MidHook(reinterpret_cast<void*>(g_muzzleFirst.address() + kMuzzleInstanceReady),
                         &MuzzleFirstPersonMaskHandler);
        }
        if (g_muzzleSecond) {
            api->MidHook(reinterpret_cast<void*>(g_muzzleSecond.address() + kMuzzleInstanceReady),
                         &MuzzleFirstPersonMaskHandler);
        }
    }

    // The push half of the layer, and the same entry as a census of every component carrying a
    // near-pass mask.
    if (g_layerPush) {
        g_pushFirstPersonLayer = reinterpret_cast<PushFirstPersonLayerFn>(g_layerPush.address());
        api->MidHook(reinterpret_cast<void*>(g_layerPush.address()),
                     &RememberFirstPersonComponent);
    }

    if (g_layerToggle) {
        api->MidHook(reinterpret_cast<void*>(g_layerToggle.address()),
                     &HoldFirstPersonLayerHandler);
    }

    if (g_compassMarker) {
        api->MidHook(reinterpret_cast<void*>(g_compassMarker.address()), &CompassMarkerHandler);
    }

    if (g_widescreen) {
        api->MidHook(reinterpret_cast<void*>(g_widescreen.address() + kWidescreenBranch),
                     &WidescreenHandler);
    }

    if (g_cinematic) {
        api->MidHook(reinterpret_cast<void*>(g_cinematic.address()), &CinematicHandler);
    }

    if (g_beautifier) {
        api->MidHook(reinterpret_cast<void*>(g_beautifier.address()), &BeautifierHandler);
    }

    if (g_cameraBlend) {
        api->MidHook(reinterpret_cast<void*>(g_cameraBlend.address()), &CameraBlendHandler);
    }

    if (g_pawnCamera) {
        api->MidHook(reinterpret_cast<void*>(g_pawnCamera.address() + kPawnCameraStored),
                     &NarrowHandler);
    }

    // Hang gliders: the camera's FOV is the vehicle's own angle, pushed into the pawn's base
    // channel as a blend target. Inlined twice, hence two patterns.
    if (g_gliderFov) {
        api->MidHook(reinterpret_cast<void*>(g_gliderFov.address() + kGliderSeat),
                     &SeatFieldOfViewHandler);
    }
    if (g_gliderSeatFov) {
        api->MidHook(reinterpret_cast<void*>(g_gliderSeatFov.address() + kGliderSeatAlternate),
                     &SeatFieldOfViewHandler);
    }

    if (g_ironsightFov) {
        api->MidHook(reinterpret_cast<void*>(g_ironsightFov.address() + kIronsightFovCopied),
                     &IronsightHandler);
    }

    char line[128];
    std::snprintf(line, sizeof(line), "fov: hooked the camera at 0x%08zX",
                  static_cast<size_t>(g_setter.address()));
    api->Log(line);
}

void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_fieldOfView = static_cast<float>(value->asSlider);
    PushPawnFieldOfView();
}

void __cdecl OnViewmodelFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_viewmodelFieldOfView = static_cast<float>(value->asSlider);
    g_viewmodelHalfTangent.store(std::tan(g_viewmodelFieldOfView * (kPi / 360.0f)),
                                 std::memory_order_relaxed);
}

void __cdecl OnIronsightFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_ironsightFieldOfView = static_cast<float>(value->asSlider);
    PushPawnFieldOfView();
}

void __cdecl OnVehicleFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_vehicleFieldOfView = static_cast<float>(value->asSlider);
    PushPawnFieldOfView();
}
