// Aiming Overhaul - how aiming and shooting look. See README.md.
#include "blur.h"
#include "engine/aim.h"
#include "engine/device_reset.h"
#include "engine/second_view.h"
#include "engine/weapon_draws.h"
#include "eyepiece.h"
#include "fcse_api.h"
#include "scope_shadow.h"
#include "scopes.h"
#include "streak.h"
#include "surroundings.h"
#include "sway.h"
#include "tracers.h"

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

    void __cdecl OnSwayChanged(const FCSE_SettingValue* value, void*) {
        AimingOverhaul::Sway::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnBlurChanged(const FCSE_SettingValue* value, void*) {
        AimingOverhaul::Blur::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnScopeShadowChanged(const FCSE_SettingValue* value, void*) {
        AimingOverhaul::ScopeShadow::SetEnabled(value->asCheckbox);
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

    float RaiseReach(const char* weaponName) {
        const AimingOverhaul::Scopes::Scope* scope = AimingOverhaul::Scopes::Find(weaponName);
        return scope != nullptr ? scope->raiseReach : 0.0f;
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !AimingOverhaul::Aim::Install(&AimingOverhaul::Sway::Drift, &RaiseReach)) {
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

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnSwayChanged, nullptr},
        {"Gun blur", FCSE_CHECKBOX(true), &OnBlurChanged, nullptr},
        {"Scope shadow", FCSE_CHECKBOX(true), &OnScopeShadowChanged, nullptr},
        {"Scope eyepiece", FCSE_CHECKBOX(true), &OnEyepieceChanged, nullptr},
        {"Scope surroundings", FCSE_CHECKBOX(true), &OnSurroundingsChanged, nullptr},
        {"Tracers", FCSE_CHECKBOX(true), &OnTracersChanged, nullptr},
    };
    api->RegisterSettings("AimingOverhaul", settings, std::size(settings));
    return true;
}
