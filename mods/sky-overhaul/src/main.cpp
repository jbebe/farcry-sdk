// Sky Overhaul - the sun, as something you cannot look at.
//
// The glare is drawn over the finished frame rather than authored into the sky's own shaders,
// because the angle it depends on is not available to any shader in the game: the engine binds the
// camera's direction to all of them and the sun's direction to none, since every draw that needs
// the sun is already placed at it. src/veil.cpp has the whole argument.
//
// Nothing here changes how the sun itself looks. The wash is added on top of a frame the engine has
// already finished.
#include "fcse_api.h"

#include "engine/sky_state.h"
#include "veil.h"

void __cdecl OnStrengthChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnSpreadChanged(const FCSE_SettingValue* value, void* userdata);

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

    // The wash needs the sun's direction, so it is not installed without it. Either failing leaves
    // the settings rows in place and inert, which is better than rows that vanish for reasons the
    // player cannot see.
    if (SkyOverhaul::SkyState::Install()) {
        SkyOverhaul::Veil::Install();
    }

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so the
    // wash is in the state it was left in by the time this returns, and again on every change.
    static const FCSE_Setting settings[] = {
        {"Sun glare strength", FCSE_SLIDER(100), &OnStrengthChanged, nullptr, nullptr, 0, 0, 200},
        {"Sun glare spread", FCSE_SLIDER(55), &OnSpreadChanged, nullptr, nullptr, 0, 10, 85},
    };
    api->RegisterSettings("Sky Overhaul", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}

void __cdecl OnStrengthChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Veil::SetStrength(value->asSlider);
}

void __cdecl OnSpreadChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    SkyOverhaul::Veil::SetSpread(value->asSlider);
}
