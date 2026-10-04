// Weapon Overhaul - how aiming down the sights looks. See README.md.
#include "blur.h"
#include "engine/aim.h"
#include "engine/device_reset.h"
#include "engine/second_view.h"
#include "engine/weapon_draws.h"
#include "eyepiece.h"
#include "fcse_api.h"
#include "scope_shadow.h"
#include "surroundings.h"
#include "sway.h"

#include <iterator>

namespace {
    void OnDeviceRelease() {
        WeaponOverhaul::Blur::ReleaseDeviceObjects();
        WeaponOverhaul::Eyepiece::ReleaseDeviceObjects();
        WeaponOverhaul::ScopeShadow::ReleaseDeviceObjects();
        WeaponOverhaul::SecondView::ReleaseDeviceObjects();
        WeaponOverhaul::Surroundings::ReleaseDeviceObjects();
        WeaponOverhaul::WeaponDraws::ReleaseDeviceObjects();
    }

    void OnGunPass(const WeaponOverhaul::Frame::Pass& pass,
                   const WeaponOverhaul::WeaponDraws::Depth& depth) {
        WeaponOverhaul::Blur::OnGunPass(pass, depth);
        WeaponOverhaul::ScopeShadow::OnGunPass(pass);
    }

    void __cdecl OnSwayChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::Sway::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnBlurChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::Blur::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnScopeShadowChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::ScopeShadow::SetEnabled(value->asCheckbox);
    }

    // A scope zooms in at once only where the surroundings keep the view outside it unzoomed.
    bool g_eyepiece = true;
    bool g_surroundings = true;

    void __cdecl OnEyepieceChanged(const FCSE_SettingValue* value, void*) {
        g_eyepiece = value->asCheckbox;
        WeaponOverhaul::Eyepiece::SetEnabled(g_eyepiece);
        WeaponOverhaul::Aim::SetZoomAtOnce(g_eyepiece && g_surroundings);
    }

    void __cdecl OnSurroundingsChanged(const FCSE_SettingValue* value, void*) {
        g_surroundings = value->asCheckbox;
        WeaponOverhaul::Surroundings::SetEnabled(g_surroundings);
        WeaponOverhaul::Aim::SetZoomAtOnce(g_eyepiece && g_surroundings);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !WeaponOverhaul::Aim::Install(&WeaponOverhaul::Sway::Drift)) {
        return false;
    }

    // Past the first hook a refusal would unload the plugin with that hook live, so this only logs.
    const WeaponOverhaul::WeaponDraws::Listener listener = {
        &WeaponOverhaul::Surroundings::BeforeColour,
        &OnGunPass,
        &WeaponOverhaul::Eyepiece::BeforeGunDraw,
        &WeaponOverhaul::Eyepiece::OnComposite,
    };
    if (!WeaponOverhaul::DeviceReset::Install(&OnDeviceRelease) ||
        !WeaponOverhaul::WeaponDraws::Install(listener)) {
        api->Log("nothing is drawn on the gun: a Direct3D seam could not be hooked");
    }
    if (!WeaponOverhaul::SecondView::Install(&WeaponOverhaul::Surroundings::Want)) {
        api->Log("a scope's surroundings are not drawn: the second view could not be hooked");
    }

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnSwayChanged, nullptr},
        {"Gun blur", FCSE_CHECKBOX(true), &OnBlurChanged, nullptr},
        {"Scope shadow", FCSE_CHECKBOX(true), &OnScopeShadowChanged, nullptr},
        {"Scope eyepiece", FCSE_CHECKBOX(true), &OnEyepieceChanged, nullptr},
        {"Scope surroundings", FCSE_CHECKBOX(true), &OnSurroundingsChanged, nullptr},
    };
    api->RegisterSettings("WeaponOverhaul", settings, std::size(settings));
    return true;
}
