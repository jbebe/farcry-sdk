// Weapon Overhaul - how aiming down the sights looks. See README.md.
#include "engine/aim.h"
#include "fcse_api.h"
#include "sway.h"

#include <iterator>

namespace {
    void __cdecl OnSwayChanged(const FCSE_SettingValue* value, void*) {
        WeaponOverhaul::Sway::SetEnabled(value->asCheckbox);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !WeaponOverhaul::Aim::Install(&WeaponOverhaul::Sway::Eye)) {
        return false;
    }

    const FCSE_Setting settings[] = {
        {"Sway", FCSE_CHECKBOX(true), &OnSwayChanged, nullptr},
    };
    api->RegisterSettings("WeaponOverhaul", settings, std::size(settings));
    return true;
}
