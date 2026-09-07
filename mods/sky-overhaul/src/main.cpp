// Sky Overhaul - the sun, as something you cannot look at.
//
// Nothing here changes how the sun itself looks. The glare is added over a frame the engine has
// already finished. See README.md for what is built and what is not.
#include "fcse_api.h"

#include "clouds.h"
#include "dazzle.h"
#include "engine/cloud_layer.h"
#include "engine/device_reset.h"
#include "engine/frame.h"
#include "engine/sky_state.h"

namespace {
    using SliderFn = void (*)(int);

    void OnDeviceRelease() {
        SkyOverhaul::Dazzle::ReleaseDeviceObjects();
        SkyOverhaul::Clouds::ReleaseDeviceObjects();
    }

    // One frame, two effects. Each decides for itself whether the pass is one it wants.
    void OnScenePass(const SkyOverhaul::Frame::Pass& pass) {
        SkyOverhaul::Clouds::OnScenePass(pass);
        SkyOverhaul::Dazzle::OnScenePass(pass);
    }

    void OnFinalPass(const SkyOverhaul::Frame::Pass& pass) {
        SkyOverhaul::Dazzle::OnFinalPass(pass);
    }

    // Every row is one slider feeding one setter, so the setter itself is the userdata FCSE hands
    // back and one callback serves them all.
    void __cdecl OnSliderChanged(const FCSE_SettingValue* value, void* userdata) {
        reinterpret_cast<SliderFn>(userdata)(value->asSlider);
    }

    void* Setter(SliderFn setter) {
        return reinterpret_cast<void*>(setter);
    }

    // Index order is what the callback below switches on; the file stores the label.
    const char* const kCloudModes[] = {"Engine", "Off", "Overhaul"};

    void __cdecl OnCloudsChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::CloudLayer::SetMode(value->asChoice == 0
                                             ? SkyOverhaul::CloudLayer::Mode::Engine
                                             : SkyOverhaul::CloudLayer::Mode::Off);
        SkyOverhaul::Clouds::SetEnabled(value->asChoice == 2);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        // FCSE logs the refusal.
        return false;
    }

    // Wires up the address library and the pattern scanner behind FCSE::Relocation, which is how
    // everything here finds the code it reads - so a failure leaves nothing that could work.
    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("Sky Overhaul loaded");

    // Both effects draw into a frame the engine owns and hold objects on its device, so neither is
    // installed without the seam that follows the frame and the one that lets go before a reset:
    // a plugin holding a render target through a reset would break the reset itself.
    if (SkyOverhaul::DeviceReset::Install(&OnDeviceRelease) &&
        SkyOverhaul::Frame::Install(&OnScenePass, &OnFinalPass)) {
        SkyOverhaul::Dazzle::Install();
        SkyOverhaul::Clouds::Install();
    }

    // The two publishers, which draw nothing. The sun's direction is the glare's, and the cloud
    // layer's lighting is what a cloud of our own is lit by.
    SkyOverhaul::SkyState::Install();
    SkyOverhaul::CloudLayer::Install();

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so the
    // glare is in the state it was left in by the time this returns, and again on every change.
    using namespace SkyOverhaul;
    static const FCSE_Setting settings[] = {
        {"Clouds", FCSE_CHOICE(0), &OnCloudsChanged, nullptr, kCloudModes,
         sizeof(kCloudModes) / sizeof(kCloudModes[0])},
        {"Cloud base", FCSE_SLIDER(1200), &OnSliderChanged, Setter(&Clouds::SetBaseAltitude),
         nullptr, 0, 100, 4000},
        {"Sun glare strength", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Dazzle::SetStrength),
         nullptr, 0, 0, 200},
        {"Sun glare spread", FCSE_SLIDER(57), &OnSliderChanged, Setter(&Dazzle::SetSpread), nullptr,
         0, 10, 180},
        {"Sun glare falloff", FCSE_SLIDER(20), &OnSliderChanged, Setter(&Dazzle::SetFalloff),
         nullptr, 0, 10, 80},
        {"Sun glare veil", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Dazzle::SetVeil), nullptr, 0,
         0, 100},
        {"Sun glare contrast", FCSE_SLIDER(195), &OnSliderChanged, Setter(&Dazzle::SetContrast),
         nullptr, 0, 0, 400},
        {"Sun glare desaturation", FCSE_SLIDER(129), &OnSliderChanged,
         Setter(&Dazzle::SetDesaturation), nullptr, 0, 0, 300},
        {"Sun elevation ramp", FCSE_SLIDER(40), &OnSliderChanged,
         Setter(&Dazzle::SetElevationRamp), nullptr, 0, 1, 45},
        {"Afterimage strength", FCSE_SLIDER(100), &OnSliderChanged,
         Setter(&Dazzle::SetAfterimageStrength), nullptr, 0, 0, 100},
        {"Afterimage seconds", FCSE_SLIDER(10), &OnSliderChanged,
         Setter(&Dazzle::SetAfterimageSeconds), nullptr, 0, 1, 10},
        {"Afterimage darkness", FCSE_SLIDER(90), &OnSliderChanged,
         Setter(&Dazzle::SetAfterimageDarkness), nullptr, 0, 0, 100},
        {"Afterimage tint", FCSE_SLIDER(35), &OnSliderChanged, Setter(&Dazzle::SetAfterimageTint),
         nullptr, 0, 0, 100},
        {"Afterimage saturation", FCSE_SLIDER(30), &OnSliderChanged,
         Setter(&Dazzle::SetAfterimageSaturation), nullptr, 0, 0, 100},
        {"Afterimage size", FCSE_SLIDER(25), &OnSliderChanged, Setter(&Dazzle::SetAfterimageSize),
         nullptr, 0, 5, 100},
        {"Afterimage haze", FCSE_SLIDER(35), &OnSliderChanged, Setter(&Dazzle::SetAfterimageHaze),
         nullptr, 0, 0, 100},
    };
    api->RegisterSettings("Sky Overhaul", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}
