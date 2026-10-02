// Flashlight - a spot light at the player's head for Far Cry 2. See README.md.
#include "flashlight.h"
#include "fcse_api.h"

#include <iterator>

namespace {
    void __cdecl OnConeChanged(const FCSE_SettingValue* value, void*) { Flashlight::SetCone(value->asChoice); }

    void __cdecl OnShadowsChanged(const FCSE_SettingValue* value, void*) { Flashlight::SetShadows(value->asCheckbox); }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    if (!Flashlight::Install()) {
        return false;
    }

    const FCSE_Setting settings[] = {
        {"Cone", FCSE_CHOICE(Flashlight::kDefaultCone), &OnConeChanged, nullptr, Flashlight::kConeLabels,
         static_cast<uint32_t>(std::size(Flashlight::kConeLabels))},
        {"Shadows", FCSE_CHECKBOX(true), &OnShadowsChanged, nullptr},
    };
    api->RegisterSettings("Flashlight", settings, std::size(settings));
    return true;
}
