// Field of view, on foot. Weapon and arms, sights, vehicles and computers keep the game's own.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/display/fov.ixx, which in
// turn credits FoxAhead's Far Cry 2 Multi Fixer for the original fFOV substitution.
//
// Far Cry 2 renders first person at 75 degrees and offers no way to change it. The value arrives
// through CCameraComponent's fFOV property setter, which takes degrees on the stack and stores
// radians at this+0x70, so substituting the argument is all the base setting takes.
//
// The rest of this file is what makes that safe. A raised field of view is not one number: the
// weapon and arms draw in a second, nearer pass with a projection of their own, which has to follow
// the camera wherever a first-person scene puts the hands on the world; muzzle particles are split
// between the two passes and come apart the moment the two projections differ; and the map's
// markers move between passes when the player is in a vehicle. Each of those is one more site here,
// and skipping any of them ships a visible break rather than a wider view.
//
// Two principles hold throughout. Fields of view are scaled in tangent space, because that is how
// the engine's own widescreen stretch works and anything else disagrees with it at the edges. And
// every pointer taken out of the engine is page-checked and vtable-checked before use, because the
// pawn that owned it does not survive a load.
#include "fcse_api.h"

#include "engine/memory_probe.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <mutex>

namespace {
    constexpr float kPi = 3.14159265f;

    // Degrees, as the setting delivers it.
    float g_fieldOfView = 75.0f;

    float DegreesToRadians(float degrees) {
        return degrees * (kPi / 180.0f);
    }

    // From a half-angle tangent `weight` of the way to a field of view, in tangent space as the
    // engine's own widescreen stretch works. All the way, the target itself: the tan/atan round
    // trip lands a few ulps off it.
    float BlendFieldOfView(float fromTan, float to, float weight) {
        weight = std::clamp(weight, 0.0f, 1.0f);
        if (weight >= 1.0f) {
            return to;
        }

        return 2.0f * std::atan(fromTan + (std::tan(to * 0.5f) - fromTan) * weight);
    }

    // Active, on CCameraComponent, reached from the secondary base the update is handed.
    constexpr uintptr_t kCameraActive = 0x0C;

    // The frame delta, the update's first argument.
    constexpr uintptr_t kCameraDeltaTime = 0x08;

    // Roughly a mount animation's length: the near pass keeps following the camera this long after
    // a scene, or as long as the scene lasted if that was shorter.
    constexpr float kSceneHoldSeconds = 0.35f;

    // A frame delta past this is a load or a hitch, and stepping by it would snap.
    constexpr float kMaxFrameStep = 0.1f;

    // First-person camera update only, so no atomics. Keyed on the component and never on the
    // pooled camera state, where an owner test only passes on the frames the buffer is reused.
    float g_sceneHold = 0.0f;
    uintptr_t g_sceneHoldCamera = 0;
    bool g_sceneHoldFromCutscene = false;

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

    enum SceneContext : uint32_t {
        kSceneNone = 0,
        kSceneLadder,
        kSceneCutscene,
    };

    // The player's pawn only, so an AI on a ladder does not count. Stamped with an expiry rather
    // than latched: quickloading out of a scene destroys the pawn with no transition to clear it.
    std::atomic<uint32_t> g_sceneContext{kSceneNone};
    std::atomic<uint32_t> g_sceneStamp{0};

    // Long enough to ride out a stutter, short enough that a load ends the scene unseen.
    constexpr uint32_t kSceneFreshnessMs = 250;

    // Whether the near pass follows the camera this frame, published by the pawn camera hook.
    std::atomic<bool> g_nearPassFollowsCamera{false};

    // The vehicle channel's weight, sampled once a frame: zero on foot, one in a seat.
    std::atomic<float> g_vehicleFovWeight{0.0f};

    bool SceneStateIsStale() {
        return GetTickCount() - g_sceneStamp.load(std::memory_order_relaxed) > kSceneFreshnessMs;
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

    // The engine's own widescreen stretch factor.
    constexpr float kWidescreenStretch = 0.75f;

    // tan(FOV / 2) for the game's own 75 degrees, the world's and the viewmodel's alike.
    constexpr float kStockHalfTangent = 0.76732699f;

    // How far the sights are up, sampled once a frame: zero down, one fully raised.
    std::atomic<float> g_ironsightBlend{0.0f};

    // Weapon setup, past the FLD and the FSTP that copy the weapon's ironsight FOV into the pawn's
    // channel, six bytes and three.
    constexpr ptrdiff_t kWeaponSetupReady = 9;

    // The first-person camera's own FOV, on the primary base. Not 0x6C, the model's FOV: writing
    // that ties the world's field of view to the viewmodel's.
    constexpr uintptr_t kPawnCameraFieldOfView = 0x70;

    // The camera's widescreen weight, on the primary base. Only a computer fades it: to zero as the
    // player sits down at one, back to one over two seconds as he gets up.
    constexpr uintptr_t kPawnCameraWidescreenWeight = 0x5C;

    // The setting, handed back to the game's own as far as a computer has faded that weight, so the
    // field of view and the game's zoom move as one.
    float PawnCameraFieldOfView(uintptr_t camera) {
        return BlendFieldOfView(kStockHalfTangent, DegreesToRadians(g_fieldOfView),
                                *reinterpret_cast<float*>(camera + kPawnCameraWidescreenWeight));
    }

    // The pawn's FOV channels, blended from a CPawnFOV rather than read where they are set. Two
    // 0x1C records: ironsight at 0x08, base at 0x24.
    constexpr uintptr_t kPawnFovIronsightWeight = 0x18;
    constexpr uintptr_t kPawnFovBaseWeight = 0x34;

    // The player's FOV channels as of the last camera frame, compared but never dereferenced.
    std::atomic<uintptr_t> g_pawnFieldOfView{0};

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
        // A stale scene state is a load or a teardown, so the table is dropped and the census
        // fills it again.
        if (SceneStateIsStale()) {
            RestoreFirstPersonSet();
            ForgetFirstPersonSet();
            return;
        }

        // A seat is the engine's own split, already answered by the near pass fade, and suppressing
        // there fights it. Boats and gliders reach a scene context while mounting.
        const bool scene = g_sceneContext.load(std::memory_order_relaxed) == kSceneCutscene &&
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

        if (SceneStateIsStale()) {
            return false;
        }

        return g_sceneContext.load(std::memory_order_relaxed) != kSceneNone;
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

        // In tangent space, to match the game's own widescreen scaling. Not named `near`: windef.h
        // still defines that as a macro.
        const float cameraTan = std::tan(cameraFov * 0.5f);
        const float nearTan = kStockHalfTangent * kWidescreenStretch * aspect;

        // A viewmodel FOV above the world's is legitimate, except with the sights up, where
        // widening the near pass back out holds the gun off the eye.
        if (cameraTan <= 0.0f ||
            (nearTan > cameraTan && g_ironsightBlend.load(std::memory_order_relaxed) > 0.0f)) {
            return cameraFov;
        }

        // In a seat the near pass follows the camera, or the hands are not where the wheel is.
        // Faded over the seat's own transition time rather than switched.
        return BlendFieldOfView(nearTan, cameraFov,
                                g_vehicleFovWeight.load(std::memory_order_relaxed));
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

    // With the Widescreen option off the field of view goes unwidened, so the branch is forced. Its
    // weight is left to the game, whose only fade of it is a computer's zoom.
    void WidescreenHandler(FCSE_MidHookContext* ctx) {
        constexpr uintptr_t kZeroFlag = 0x40;

        ctx->eflags &= ~kZeroFlag;
    }

    // Whether the player is on a ladder or in a first-person scene, read from the beautifier's
    // context at its update's entry, one frame old.
    void BeautifierHandler(FCSE_MidHookContext* ctx) {
        const uintptr_t component = ctx->ecx - 4;

        // Every AI updates through here, so the type half is checked first.
        if (KindOf(*reinterpret_cast<uintptr_t*>(component + kTypeBeautifier)) != kKindPlayer) {
            return;
        }

        uint32_t context = kSceneNone;
        switch (KindOf(*reinterpret_cast<uintptr_t*>(component + kContextBeautifier))) {
        case kKindLadder:
            context = kSceneLadder;
            break;
        case kKindCutscene:
            context = kSceneCutscene;
            break;
        default:
            break;
        }

        g_sceneContext.store(context, std::memory_order_relaxed);
        g_sceneStamp.store(GetTickCount(), std::memory_order_relaxed);
    }

    // At the head of the frame's FOV blend: the camera's own field is rebuilt every frame and the
    // setter only seeds it. ESI is the camera, EAX the CPawnFOV.
    void CameraBlendHandler(FCSE_MidHookContext* ctx) {
        // Before the MOVSS this stands on, so the blend uses it on this frame.
        *reinterpret_cast<float*>(ctx->esi + kPawnCameraFieldOfView) =
            PawnCameraFieldOfView(ctx->esi);

        const uintptr_t block = ctx->eax;

        g_pawnFieldOfView.store(block, std::memory_order_relaxed);
        g_ironsightBlend.store(*reinterpret_cast<float*>(block + kPawnFovIronsightWeight),
                               std::memory_order_relaxed);
        g_vehicleFovWeight.store(*reinterpret_cast<float*>(block + kPawnFovBaseWeight),
                                 std::memory_order_relaxed);

        // The swing deadline, and the scene's layer bookkeeping: this is the one hook running every
        // frame on the gameplay thread, and the set's first-use allocation faults from the near
        // pass hook.
        ReleaseWithheldLayerIfStale();
        UpdateFirstPersonSetForScene();
    }

    // CCameraPawnComponent::Update for first person. The scene hold is tied to the camera it started
    // on.
    void PawnCameraHandler(FCSE_MidHookContext* ctx) {
        // Only the camera actually driving the view; an inactive one still updates.
        if (*reinterpret_cast<uint8_t*>(ctx->edi + kCameraActive) == 0) {
            return;
        }

        // On the update's own delta, so a paused game does not age the hold.
        const float frame =
            std::clamp(*reinterpret_cast<float*>(ctx->ebp + kCameraDeltaTime), 0.0f, kMaxFrameStep);
        StepWithheldLayer(frame);

        // The pawn that owned the state is gone, so the scene is dropped outright.
        if (SceneStateIsStale()) {
            g_sceneHold = 0.0f;
            g_sceneHoldFromCutscene = false;
            g_nearPassFollowsCamera.store(false, std::memory_order_relaxed);
            return;
        }

        const uint32_t context = g_sceneContext.load(std::memory_order_relaxed);

        // A different active camera starts again rather than carrying a hold across views.
        if (g_sceneHoldCamera != ctx->edi) {
            g_sceneHoldCamera = ctx->edi;
            g_sceneHold = 0.0f;
        }

        // The near pass follows the camera through a scene, a ladder mounted through one, and the
        // hold after, or the viewmodel jumps out of every scene.
        if (context == kSceneCutscene) {
            g_sceneHoldFromCutscene = true;
        }

        g_nearPassFollowsCamera.store(context == kSceneCutscene ||
                                          (g_sceneHold > 0.0f && g_sceneHoldFromCutscene),
                                      std::memory_order_relaxed);

        g_sceneHold = std::clamp(g_sceneHold + (context != kSceneNone ? frame : -frame), 0.0f,
                                 kSceneHoldSeconds);
        if (g_sceneHold <= 0.0f) {
            g_sceneHoldFromCutscene = false;
            g_nearPassFollowsCamera.store(false, std::memory_order_relaxed);
        }
    }

    // Weapon setup, where the weapon hands its ironsight FOV to the pawn: whether a melee weapon is
    // now in hand. EAX is the weapon's properties.
    void WeaponSetupHandler(FCSE_MidHookContext* ctx) {
        // Every pawn arms itself through here, so the player's channel is picked out first. An
        // unset handle is accepted, the session's first setup being his.
        const uintptr_t player = g_pawnFieldOfView.load(std::memory_order_relaxed);
        if (player != 0 && ctx->edi != player) {
            return;
        }

        // EDI is the pawn's FOV channels, taken so the filter holds before the first camera frame.
        g_pawnFieldOfView.store(ctx->edi, std::memory_order_relaxed);
        RememberMeleeWeapon(*reinterpret_cast<uint32_t*>(ctx->eax + kWeaponClass));
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
    FCSE::Relocation<uint8_t*> g_beautifier{FCSE::Pattern(
        "55 56 8B E9 57 8B 7D 04 8B 77 0C 83 47 08 01 8B CE E8 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 00 "
        "75 07")};
    FCSE::Relocation<uint8_t*> g_cameraBlend{
        FCSE::Pattern("F3 0F 10 4E 70 F3 0F 10 47 14 0F 57 D2 F3 0F 5C C1 F3 0F 59 47 10")};
    FCSE::Relocation<uint8_t*> g_pawnCamera{
        FCSE::Pattern("D9 47 6C 8B 4C 24 3C D9 5E 30 8A 9E 80 00 00 00")};
    FCSE::Relocation<uint8_t*> g_weaponSetup{FCSE::Pattern("D9 80 E4 00 00 00 D9 5F 1C")};

    // Offsets into the matches above, each naming the instruction the hook stands on.
    constexpr ptrdiff_t kViewmodelFovPushed = 9;
    constexpr ptrdiff_t kFirstPersonMaskCall = 0x12;
    constexpr ptrdiff_t kMuzzleInstanceReady = 0x22;
    constexpr ptrdiff_t kWidescreenBranch = 10;
    constexpr ptrdiff_t kPawnCameraStored = 10;

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

    if (g_beautifier) {
        api->MidHook(reinterpret_cast<void*>(g_beautifier.address()), &BeautifierHandler);
    }

    if (g_cameraBlend) {
        api->MidHook(reinterpret_cast<void*>(g_cameraBlend.address()), &CameraBlendHandler);
    }

    if (g_pawnCamera) {
        api->MidHook(reinterpret_cast<void*>(g_pawnCamera.address() + kPawnCameraStored),
                     &PawnCameraHandler);
    }

    if (g_weaponSetup) {
        api->MidHook(reinterpret_cast<void*>(g_weaponSetup.address() + kWeaponSetupReady),
                     &WeaponSetupHandler);
    }

    FCSE::Logf("fov: hooked the camera at 0x%08zX", static_cast<size_t>(g_setter.address()));
}

void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_fieldOfView = static_cast<float>(value->asSlider);
}
