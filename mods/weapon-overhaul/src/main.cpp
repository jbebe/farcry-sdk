// Weapon Overhaul - how aiming down the sights looks. See README.md.
#include "blur.h"
#include "engine/aim.h"
#include "engine/device_reset.h"
#include "engine/screen_draw.h"
#include "engine/weapon_draws.h"
#include "fcse_api.h"
#include "scope_shadow.h"
#include "sway.h"

#include <iterator>

namespace {
    void OnDeviceRelease() {
        WeaponOverhaul::Blur::ReleaseDeviceObjects();
        WeaponOverhaul::ScopeShadow::ReleaseDeviceObjects();
        WeaponOverhaul::ScreenDraw::ReleaseDeviceObjects();
        WeaponOverhaul::WeaponDraws::ReleaseDeviceObjects();
    }

    void OnGunPass(const WeaponOverhaul::Frame::Pass& pass,
                   const WeaponOverhaul::WeaponDraws::Depth& depth) {
        WeaponOverhaul::Blur::OnGunPass(pass, depth);
        WeaponOverhaul::ScopeShadow::OnGunPass(pass, depth);
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
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !WeaponOverhaul::Aim::Install(&WeaponOverhaul::Sway::Drift)) {
        return false;
    }

    // Past the first hook a refusal would unload the plugin with that hook live, so this only logs.
    if (!WeaponOverhaul::DeviceReset::Install(&OnDeviceRelease) ||
        !WeaponOverhaul::WeaponDraws::Install(&OnGunPass)) {
        api->Log("nothing is drawn on the gun: a Direct3D seam could not be hooked");
    }

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnSwayChanged, nullptr},
        {"Gun blur", FCSE_CHECKBOX(true), &OnBlurChanged, nullptr},
        {"Scope shadow", FCSE_CHECKBOX(true), &OnScopeShadowChanged, nullptr},
    };
    api->RegisterSettings("WeaponOverhaul", settings, std::size(settings));
    return true;
}
