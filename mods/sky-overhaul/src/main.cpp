// Sky Overhaul - the sun, as something you cannot look at.
//
// Nothing here changes how the sun itself looks. The glare is added over a frame the engine has
// already finished. See README.md for what is built and what is not.
#include "fcse_api.h"

#include "dazzle.h"
#include "engine/device_reset.h"
#include "engine/sky_state.h"

void OnDeviceRelease();
void __cdecl OnStrengthChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnSpreadChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnFalloffChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnContrastChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnDesaturationChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnVeilChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnElevationRampChanged(const FCSE_SettingValue* value, void* userdata);

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // FCSE logs the refusal
    }

    // Wires up the address library and the pattern scanner behind FCSE::Relocation, which is how
    // everything here finds the code it reads - so a failure leaves nothing that could work.
    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("Sky Overhaul loaded");

    // The glare needs the sun's direction, and it holds a copy of the frame that has to be
    // surrendered before the engine resets the device. Without either seam it does not install:
    // a plugin holding a render target through a reset would break the reset itself.
    if (SkyOverhaul::SkyState::Install() && SkyOverhaul::DeviceReset::Install(&OnDeviceRelease)) {
        SkyOverhaul::Dazzle::Install();
    }

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so the
    // glare is in the state it was left in by the time this returns, and again on every change.
    static const FCSE_Setting settings[] = {
        {"Sun glare strength", FCSE_SLIDER(100), &OnStrengthChanged, nullptr, nullptr, 0, 0, 200},
        {"Sun glare spread", FCSE_SLIDER(57), &OnSpreadChanged, nullptr, nullptr, 0, 10, 180},
        {"Sun glare falloff", FCSE_SLIDER(20), &OnFalloffChanged, nullptr, nullptr, 0, 10, 80},
        {"Sun glare veil", FCSE_SLIDER(100), &OnVeilChanged, nullptr, nullptr, 0, 0, 100},
        {"Sun glare contrast", FCSE_SLIDER(195), &OnContrastChanged, nullptr, nullptr, 0, 0, 400},
        {"Sun glare desaturation", FCSE_SLIDER(129), &OnDesaturationChanged, nullptr, nullptr, 0, 0,
         300},
        {"Sun elevation ramp", FCSE_SLIDER(40), &OnElevationRampChanged, nullptr, nullptr, 0, 1,
         45},
    };
    api->RegisterSettings("Sky Overhaul", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}

void OnDeviceRelease() {
    SkyOverhaul::Dazzle::ReleaseDeviceObjects();
}

void __cdecl OnStrengthChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetStrength(value->asSlider);
}

void __cdecl OnSpreadChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetSpread(value->asSlider);
}

void __cdecl OnFalloffChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetFalloff(value->asSlider);
}

void __cdecl OnContrastChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetContrast(value->asSlider);
}

void __cdecl OnDesaturationChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetDesaturation(value->asSlider);
}

void __cdecl OnVeilChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetVeil(value->asSlider);
}

void __cdecl OnElevationRampChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Dazzle::SetElevationRamp(value->asSlider);
}
