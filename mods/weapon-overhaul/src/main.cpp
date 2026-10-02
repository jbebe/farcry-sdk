// Weapon Overhaul - how aiming down the sights looks. See README.md.
#include "blur.h"
#include "engine/aim.h"
#include "engine/device_reset.h"
#include "engine/frame.h"
#include "engine/trace.h"
#include "engine/weapon_draws.h"
#include "fcse_api.h"
#include "sway.h"

#include <iterator>

namespace {
    void OnDeviceRelease() {
        WeaponOverhaul::Blur::ReleaseDeviceObjects();
        WeaponOverhaul::WeaponDraws::ReleaseDeviceObjects();
    }

    void __cdecl OnSwayChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::Sway::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnBlurChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::Blur::SetEnabled(value->asCheckbox);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !WeaponOverhaul::Aim::Install(&WeaponOverhaul::Sway::Eye)) {
        return false;
    }

    // Nothing is drawn without the seam that frees it before a reset, and a plugin refused past
    // its first hook is unloaded with that hook live, so a failure here only costs the blur.
    if (!WeaponOverhaul::DeviceReset::Install(&OnDeviceRelease) ||
        !WeaponOverhaul::Frame::Install(&WeaponOverhaul::Blur::OnScenePass) ||
        !WeaponOverhaul::WeaponDraws::Install()) {
        api->Log("the gun blur is off: a Direct3D seam it needs could not be hooked");
    }
    WeaponOverhaul::Trace::Install();

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnSwayChanged, nullptr},
        {"Gun blur", FCSE_CHECKBOX(true), &OnBlurChanged, nullptr},
    };
    api->RegisterSettings("WeaponOverhaul", settings, std::size(settings));
    return true;
}
