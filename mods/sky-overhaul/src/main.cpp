// Sky Overhaul - the sun, as something you cannot look at.
//
// Nothing here changes how the sun itself looks. The glare is added over a frame the engine has
// already finished. See README.md for what is built and what is not.
#include "devtools_api.h"
#include "fcse_api.h"
#include "imgui.h"

#include "clouds.h"
#include "dazzle.h"
#include "engine/cloud_layer.h"
#include "engine/device_reset.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "sky.h"

namespace {
    using SliderFn = void (*)(int);

    void OnDeviceRelease() {
        SkyOverhaul::Dazzle::ReleaseDeviceObjects();
        SkyOverhaul::Clouds::ReleaseDeviceObjects();
        SkyOverhaul::Sky::ReleaseDeviceObjects();
        SkyOverhaul::DrawGuard::ReleaseDeviceObjects();
    }

    // One frame, three effects. Each decides for itself whether the pass is one it wants. The sky
    // has already drawn itself from inside the pass by the time this runs.
    void OnScenePass(const SkyOverhaul::Frame::Pass& pass) {
        SkyOverhaul::Sky::OnScenePass(pass);
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

    FCSE_Setting Hidden(FCSE_Setting setting) {
        setting.flags |= FCSE_SettingFlag_Hidden;
        return setting;
    }

    // Index order is what the callback below switches on; the file stores the label.
    const char* const kSkyModes[] = {"Engine", "Overhaul"};
    void __cdecl OnSkyChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Sky::SetEnabled(value->asChoice == 1);
    }

    const char* const kCloudModes[] = {"Engine", "Off", "Overhaul"};
    void __cdecl OnCloudsChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::CloudLayer::SetMode(value->asChoice == 0
                                             ? SkyOverhaul::CloudLayer::Mode::Engine
                                             : SkyOverhaul::CloudLayer::Mode::Off);
        SkyOverhaul::Clouds::SetEnabled(value->asChoice == 2);
    }

    // The window in DevTools' overlay, a placeholder until there is tuning to put in it.
    void DrawOverlayWindow(void*) {
        ImGui::TextUnformatted("Sky Overhaul's tuning will be here.");
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

    // The effects draw into a frame the engine owns and hold objects on its device, so none is
    // installed without the seam that follows the frame and the one that lets go before a reset:
    // a plugin holding a render target through a reset would break the reset itself.
    if (SkyOverhaul::DeviceReset::Install(&OnDeviceRelease) &&
        SkyOverhaul::Frame::Install(&OnScenePass, &OnFinalPass)) {
        SkyOverhaul::Clouds::Install();
        // Watches the draws inside a pass rather than the passes themselves, because that is where
        // the dome is and a sky has to go under everything drawn after it.
        SkyOverhaul::Sky::Install();
    }

    // The publisher every effect reads, which draws nothing: the sun, the moon and the weather as
    // the cloud layer is lit by them.
    SkyOverhaul::CloudLayer::Install();

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so the
    // glare is in the state it was left in by the time this returns, and again on every change.
    using namespace SkyOverhaul;
    static const FCSE_Setting settings[] = {
        {"Sky", FCSE_CHOICE(0), &OnSkyChanged, nullptr, kSkyModes,
         sizeof(kSkyModes) / sizeof(kSkyModes[0])},
        {"Sky haze", FCSE_SLIDER(40), &OnSliderChanged, Setter(&Sky::SetHaze), nullptr, 0, 0, 100},
        {"Sky brightness", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Sky::SetBrightness), nullptr,
         0, 0, 300},
        {"Horizon gradient", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Sky::SetHorizonGradient),
         nullptr, 0, 0, 100},
        {"Far horizon brightness", FCSE_SLIDER(30), &OnSliderChanged,
         Setter(&Sky::SetFarHorizonBrightness), nullptr, 0, 0, 100},
        {"Zenith hold", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Sky::SetZenithHold), nullptr, 0,
         0, 100},
        {"Below horizon brown", FCSE_SLIDER(50), &OnSliderChanged, Setter(&Sky::SetGroundBrown),
         nullptr, 0, 0, 100},
        {"Night sky", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Sky::SetNightSky), nullptr, 0, 0,
         300},
        {"Twilight sky", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Sky::SetTwilightSky), nullptr,
         0, 0, 300},
        {"Horizon match", FCSE_SLIDER(100), &OnSliderChanged, Setter(&FogTint::SetMatch), nullptr,
         0, 0, 100},
        {"Clouds", FCSE_CHOICE(0), &OnCloudsChanged, nullptr, kCloudModes,
         sizeof(kCloudModes) / sizeof(kCloudModes[0])},
        {"Cloud base", FCSE_SLIDER(1200), &OnSliderChanged, Setter(&Clouds::SetBaseAltitude),
         nullptr, 0, 100, 4000},
        {"Cloud thickness", FCSE_SLIDER(700), &OnSliderChanged, Setter(&Clouds::SetThickness),
         nullptr, 0, 100, 3000},
        {"Cloud coverage", FCSE_SLIDER(45), &OnSliderChanged, Setter(&Clouds::SetCoverage), nullptr,
         0, 0, 100},
        {"Cloud density", FCSE_SLIDER(40), &OnSliderChanged, Setter(&Clouds::SetDensity), nullptr,
         0, 5, 300},
        {"Cloud detail", FCSE_SLIDER(35), &OnSliderChanged, Setter(&Clouds::SetDetail), nullptr, 0,
         0, 100},
        {"Cloud size", FCSE_SLIDER(4000), &OnSliderChanged, Setter(&Clouds::SetGrain), nullptr, 0,
         500, 12000},
        {"Cloud wind", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Clouds::SetWind), nullptr, 0, 0,
         500},
        {"Cloud haze", FCSE_SLIDER(3000), &OnSliderChanged, Setter(&Clouds::SetHaze), nullptr, 0,
         300, 20000},
        {"Cirrus", FCSE_SLIDER(35), &OnSliderChanged, Setter(&Clouds::SetCirrus), nullptr, 0, 0,
         100},
        {"Cirrus opacity", FCSE_SLIDER(70), &OnSliderChanged, Setter(&Clouds::SetCirrusOpacity),
         nullptr, 0, 0, 100},
        {"Contrails", FCSE_SLIDER(60), &OnSliderChanged, Setter(&Clouds::SetContrails), nullptr, 0,
         0, 100},
        {"Moonlight", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Clouds::SetMoonlight), nullptr, 0,
         0, 100},
        {"Moon glow", FCSE_SLIDER(25), &OnSliderChanged, Setter(&Clouds::SetMoonGlow), nullptr, 0,
         0, 100},
        // The glare's, off the menu while the sky and clouds are tuned; fcse.ini still has them.
        Hidden({"Sun glare strength", FCSE_SLIDER(100), &OnSliderChanged,
                Setter(&Dazzle::SetStrength), nullptr, 0, 0, 200}),
        Hidden({"Sun glare spread", FCSE_SLIDER(57), &OnSliderChanged, Setter(&Dazzle::SetSpread),
                nullptr, 0, 10, 180}),
        Hidden({"Sun glare falloff", FCSE_SLIDER(20), &OnSliderChanged,
                Setter(&Dazzle::SetFalloff), nullptr, 0, 10, 80}),
        Hidden({"Sun glare veil", FCSE_SLIDER(100), &OnSliderChanged, Setter(&Dazzle::SetVeil),
                nullptr, 0, 0, 100}),
        Hidden({"Sun glare contrast", FCSE_SLIDER(195), &OnSliderChanged,
                Setter(&Dazzle::SetContrast), nullptr, 0, 0, 400}),
        Hidden({"Sun glare desaturation", FCSE_SLIDER(129), &OnSliderChanged,
                Setter(&Dazzle::SetDesaturation), nullptr, 0, 0, 300}),
        Hidden({"Sun elevation ramp", FCSE_SLIDER(40), &OnSliderChanged,
                Setter(&Dazzle::SetElevationRamp), nullptr, 0, 1, 45}),
        Hidden({"Afterimage strength", FCSE_SLIDER(100), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageStrength), nullptr, 0, 0, 100}),
        Hidden({"Afterimage seconds", FCSE_SLIDER(10), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageSeconds), nullptr, 0, 1, 10}),
        Hidden({"Afterimage darkness", FCSE_SLIDER(90), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageDarkness), nullptr, 0, 0, 100}),
        Hidden({"Afterimage tint", FCSE_SLIDER(35), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageTint), nullptr, 0, 0, 100}),
        Hidden({"Afterimage saturation", FCSE_SLIDER(30), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageSaturation), nullptr, 0, 0, 100}),
        Hidden({"Afterimage size", FCSE_SLIDER(25), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageSize), nullptr, 0, 5, 100}),
        Hidden({"Afterimage haze", FCSE_SLIDER(35), &OnSliderChanged,
                Setter(&Dazzle::SetAfterimageHaze), nullptr, 0, 0, 100}),
    };
    // Registered under the module name rather than a prettier one: the mod menu lists every
    // loaded plugin and then every settings group that matched none of them, so a group named
    // differently from its DLL arrives on that page twice, once empty.
    api->RegisterSettings("SkyOverhaul", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api) {
    DevTools::Overlay::AddWindow(api, "Sky Overhaul", 360.0f, 100.0f, &DrawOverlayWindow);
}
