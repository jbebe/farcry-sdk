// Aiming Overhaul - how aiming and shooting look. See README.md.
#include "blur.h"
#include "breath.h"
#include "engine/aim.h"
#include "engine/device_reset.h"
#include "engine/fire.h"
#include "engine/input.h"
#include "engine/look.h"
#include "engine/pawn.h"
#include "engine/ray.h"
#include "engine/second_view.h"
#include "engine/shot.h"
#include "engine/sprint.h"
#include "engine/weapon_draws.h"
#include "eyepiece.h"
#include "fcse_api.h"
#include "lean.h"
#include "peek.h"
#include "scope_shadow.h"
#include "scopes.h"
#include "streak.h"
#include "surroundings.h"
#include "sway.h"
#include "tracers.h"
#include "wall.h"

#include <windows.h>

#include <iterator>

namespace {
    void OnDeviceRelease() {
        AimingOverhaul::Blur::ReleaseDeviceObjects();
        AimingOverhaul::Eyepiece::ReleaseDeviceObjects();
        AimingOverhaul::ScopeShadow::ReleaseDeviceObjects();
        AimingOverhaul::SecondView::ReleaseDeviceObjects();
        AimingOverhaul::Streak::ReleaseDeviceObjects();
        AimingOverhaul::Surroundings::ReleaseDeviceObjects();
        AimingOverhaul::WeaponDraws::ReleaseDeviceObjects();
    }

    void OnGunPass(const AimingOverhaul::Frame::Pass& pass,
                   const AimingOverhaul::WeaponDraws::Depth& depth) {
        AimingOverhaul::Blur::OnGunPass(pass, depth);
        AimingOverhaul::ScopeShadow::OnGunPass(pass);
    }

    // A checkbox that switches one feature, whose SetEnabled rides in `userdata`.
    using SetEnabledFn = void (*)(bool enabled);

    void __cdecl OnToggled(const FCSE_SettingValue* value, void* setEnabled) {
        reinterpret_cast<SetEnabledFn>(setEnabled)(value->asCheckbox);
    }

    void* Toggles(SetEnabledFn setEnabled) {
        return reinterpret_cast<void*>(setEnabled);
    }

    void __cdecl OnTracersChanged(const FCSE_SettingValue* value, void*) {
        AimingOverhaul::Tracers::SetEnabled(value->asCheckbox);
        AimingOverhaul::Streak::SetEnabled(value->asCheckbox);
    }

    // A scope zooms in at once only where the surroundings keep the view outside it unzoomed: both
    // on, and the seams they draw through hooked.
    bool g_eyepiece = true;
    bool g_surroundings = true;
    bool g_hooked = false;

    void FollowZoom() {
        AimingOverhaul::Aim::SetZoomAtOnce(g_hooked && g_eyepiece && g_surroundings);
    }

    void __cdecl OnEyepieceChanged(const FCSE_SettingValue* value, void*) {
        g_eyepiece = value->asCheckbox;
        AimingOverhaul::Eyepiece::SetEnabled(g_eyepiece);
        FollowZoom();
    }

    void __cdecl OnSurroundingsChanged(const FCSE_SettingValue* value, void*) {
        g_surroundings = value->asCheckbox;
        AimingOverhaul::Surroundings::SetEnabled(g_surroundings);
        FollowZoom();
    }

    float FollowWeapon(uint8_t* weapon) {
        const AimingOverhaul::Scopes::Scope* scope = AimingOverhaul::Scopes::Follow(weapon);
        return scope != nullptr ? scope->raiseReach : 0.0f;
    }

    void OnSignal(uint32_t signal) {
        AimingOverhaul::Breath::OnSignal(signal);
        AimingOverhaul::Lean::OnSignal(signal);
        AimingOverhaul::Peek::OnSignal(signal);
    }

    // The player's pawn as the camera last saw it, and when. The camera stops seeing it out of a
    // vehicle, and a gun blocked at a wall is not refused there.
    uint8_t* g_pawn = nullptr;
    ULONGLONG g_seen = 0;
    constexpr ULONGLONG kOnFootMilliseconds = 200;

    bool OnFoot(uint8_t* pawn) {
        return pawn != nullptr && pawn == g_pawn && GetTickCount64() - g_seen < kOnFootMilliseconds;
    }

    // The lean, then the gun near walls, judged from where the lean is taking the eye so a lean out
    // past a corner does not swing the gun down on the way, then the breath and the sway.
    AimingOverhaul::Aim::Adjust OnAimFrame(uint8_t* pawn, float seconds) {
        using namespace AimingOverhaul;
        g_pawn = pawn;
        g_seen = GetTickCount64();
        float forward[3];
        Aim::Forward(forward);
        const float* eye = Aim::Eye();
        const float autoRoll = Peek::Update(seconds, eye, forward, Lean::Current());
        const float roll = Lean::Roll(seconds, !Pawn::Is(pawn, Pawn::kSprint), autoRoll);
        const Aim::Offset leanEye = Lean::Eye(roll);
        WeaponDraws::SetViewMove(roll, leanEye);
        float leaned[3];
        if (eye != nullptr) {
            Lean::Shift(forward, roll, Lean::Target(), leaned);
            for (int i = 0; i < 3; i++) {
                leaned[i] += eye[i];
            }
        }
        Wall::Update(seconds, eye != nullptr ? leaned : nullptr, forward);
        if (Wall::Blocked()) {
            Pawn::DropSights(pawn);
        }
        Breath::Update(seconds, Aim::Aiming());
        return {Sway::Drift(seconds), leanEye, Wall::Down(), roll};
    }

    // The player's shot leaves from where the lean has taken the eye.
    void MoveShot(uint8_t* pawn, float origin[3]) {
        const float roll = AimingOverhaul::Lean::Current();
        if (roll == 0.0f || !OnFoot(pawn)) {
            return;
        }
        float forward[3];
        float shift[3];
        AimingOverhaul::Aim::Forward(forward);
        AimingOverhaul::Lean::Shift(forward, 0.0f, roll, shift);
        for (int i = 0; i < 3; i++) {
            origin[i] += shift[i];
        }
    }

    // The sway's wander, with the look lowered as far as the gun is swung down.
    AimingOverhaul::Look::Angles OnLook() {
        AimingOverhaul::Look::Angles angles = AimingOverhaul::Sway::Wander();
        angles.up -= AimingOverhaul::Wall::Down();
        return angles;
    }

    bool RefuseShot(uint8_t* pawn) {
        return AimingOverhaul::Wall::Blocked() && OnFoot(pawn);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !AimingOverhaul::Aim::Install(&OnAimFrame, &FollowWeapon)) {
        return false;
    }

    // Past the first hook a refusal would unload the plugin with that hook live, so this only logs.
    const AimingOverhaul::WeaponDraws::Listener listener = {
        &AimingOverhaul::Surroundings::BeforeColour,
        &OnGunPass,
        &AimingOverhaul::Eyepiece::BeforeGunDraw,
        &AimingOverhaul::Eyepiece::OnComposite,
    };
    g_hooked = true;
    if (!AimingOverhaul::DeviceReset::Install(&OnDeviceRelease) ||
        !AimingOverhaul::WeaponDraws::Install(listener)) {
        api->Log("nothing is drawn on the gun: a Direct3D seam could not be hooked");
        g_hooked = false;
    }
    if (!AimingOverhaul::SecondView::Install(&AimingOverhaul::Surroundings::Want)) {
        api->Log("a scope's surroundings are not drawn: the second view could not be hooked");
        g_hooked = false;
    }
    FollowZoom();
    AimingOverhaul::Tracers::Install();
    AimingOverhaul::Streak::Install();
    AimingOverhaul::Look::Install(&OnLook);
    AimingOverhaul::Shot::Install(&MoveShot);
    AimingOverhaul::Ray::Install();
    AimingOverhaul::Fire::Install(&RefuseShot);
    AimingOverhaul::Input::Install(&OnSignal);
    AimingOverhaul::Sprint::Install(&AimingOverhaul::Breath::HoldsSprint);

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnToggled, Toggles(&AimingOverhaul::Sway::SetEnabled)},
        {"Hold breath", FCSE_CHECKBOX(true), &OnToggled,
         Toggles(&AimingOverhaul::Breath::SetEnabled)},
        {"Lean", FCSE_CHECKBOX(true), &OnToggled, Toggles(&AimingOverhaul::Lean::SetEnabled)},
        {"Auto lean", FCSE_CHECKBOX(true), &OnToggled, Toggles(&AimingOverhaul::Peek::SetEnabled)},
        {"Gun near walls", FCSE_CHECKBOX(true), &OnToggled,
         Toggles(&AimingOverhaul::Wall::SetEnabled)},
        {"Gun blur", FCSE_CHECKBOX(true), &OnToggled, Toggles(&AimingOverhaul::Blur::SetEnabled)},
        {"Iron sight zoom", FCSE_CHECKBOX(true), &OnToggled,
         Toggles(&AimingOverhaul::Aim::SetIronsightZoom)},
        {"Scope shadow", FCSE_CHECKBOX(true), &OnToggled,
         Toggles(&AimingOverhaul::ScopeShadow::SetEnabled)},
        {"Scope eyepiece", FCSE_CHECKBOX(true), &OnEyepieceChanged, nullptr},
        {"Scope surroundings", FCSE_CHECKBOX(true), &OnSurroundingsChanged, nullptr},
        {"Tracers", FCSE_CHECKBOX(true), &OnTracersChanged, nullptr},
    };
    api->RegisterSettings("AimingOverhaul", settings, std::size(settings));
    return true;
}
