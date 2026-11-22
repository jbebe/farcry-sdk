// Sky Overhaul - the sun, as something you cannot look at.
//
// Nothing here changes how the sun itself looks. The glare is added over a frame the engine has
// already finished. See README.md for what is built and what is not.
#include "devtools_api.h"
#include "fcse_api.h"

#include "clouds.h"
#include "dazzle.h"
#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/device_reset.h"
#include "engine/environment.h"
#include "engine/frame_copy.h"
#include "engine/frame.h"
#include "engine/known_shaders.h"
#include "engine/linear_depth.h"
#include "engine/rain_component.h"
#include "engine/rain_cover.h"
#include "engine/screen_draw.h"
#include "engine/solid_depth.h"
#include "flatten.h"
#include "foliage.h"
#include "ground.h"
#include "lightning.h"
#include "grade.h"
#include "grain.h"
#include "haze.h"
#include "night.h"
#include "occlusion.h"
#include "rain.h"
#include "rainbow.h"
#include "ripples.h"
#include "shadows.h"
#include "sky.h"
#include "sun_shadows.h"
#include "tuning.h"
#include "weather/weather.h"

#include <iterator>

namespace {
    void OnDeviceRelease() {
        SkyOverhaul::Dazzle::ReleaseDeviceObjects();
        SkyOverhaul::Clouds::ReleaseDeviceObjects();
        SkyOverhaul::Sky::ReleaseDeviceObjects();
        SkyOverhaul::Night::ReleaseDeviceObjects();
        SkyOverhaul::Shadows::ReleaseDeviceObjects();
        SkyOverhaul::Occlusion::ReleaseDeviceObjects();
        SkyOverhaul::SolidDepth::ReleaseDeviceObjects();
        SkyOverhaul::DepthTexture::ReleaseDeviceObjects();
        SkyOverhaul::FrameCopy::ReleaseDeviceObjects();
        SkyOverhaul::DrawGuard::ReleaseDeviceObjects();
        SkyOverhaul::Foliage::ReleaseDeviceObjects();
        SkyOverhaul::Grain::ReleaseDeviceObjects();
        SkyOverhaul::Haze::ReleaseDeviceObjects();
        SkyOverhaul::Flatten::ReleaseDeviceObjects();
        SkyOverhaul::Rain::ReleaseDeviceObjects();
        SkyOverhaul::Ground::ReleaseDeviceObjects();
        SkyOverhaul::Ripples::ReleaseDeviceObjects();
        SkyOverhaul::Lightning::ReleaseDeviceObjects();
        SkyOverhaul::Rainbow::ReleaseDeviceObjects();
        SkyOverhaul::KnownShaders::Forget();
    }

    // Each effect decides for itself whether the pass is one it wants. The sky has already drawn
    // itself from inside the pass by the time this runs.
    void OnScenePass(const SkyOverhaul::Frame::Pass& pass) {
        SkyOverhaul::Camera::View view;
        if (pass.sky && pass.live && SkyOverhaul::Camera::Read(pass.device, view)) {
            SkyOverhaul::Weather::SetEye(view.eye, view.direction);
        }
        SkyOverhaul::Sky::OnScenePass(pass);
        SkyOverhaul::Clouds::OnScenePass(pass);
        SkyOverhaul::Rainbow::OnScenePass(pass);
        SkyOverhaul::Shadows::OnScenePass(pass);
        SkyOverhaul::SolidDepth::OnScenePass(pass);
        SkyOverhaul::Occlusion::OnScenePass(pass);
        SkyOverhaul::Ground::OnScenePass(pass);
        SkyOverhaul::Rain::OnScenePass(pass);
        SkyOverhaul::Lightning::OnScenePass(pass);
        SkyOverhaul::Haze::OnScenePass(pass);
        SkyOverhaul::Dazzle::OnScenePass(pass);
        SkyOverhaul::Night::OnScenePass(pass);
        SkyOverhaul::SunShadows::OnScenePass(pass);
        SkyOverhaul::Flatten::OnScenePass(pass);
    }

    void OnFinalPass(const SkyOverhaul::Frame::Pass& pass) {
        SkyOverhaul::Sky::OnFinalPass(pass);
        SkyOverhaul::Dazzle::OnFinalPass(pass);
        SkyOverhaul::Grain::OnFinalPass(pass);
        SkyOverhaul::SolidDepth::OnFinalPass(pass);
    }

    // Index order is what the callbacks below switch on; fcse.ini stores the label.
    const char* const kModes[] = {"Engine", "Overhaul"};
    const char* const kCloudModes[] = {"Engine", "Off", "Overhaul"};
    const char* const kSwitchModes[] = {"Off", "On"};

    void __cdecl OnSkyChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Sky::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnCloudsChanged(const FCSE_SettingValue* value, void*) {
        using SkyOverhaul::CloudLayer::Mode;
        SkyOverhaul::CloudLayer::SetMode(value->asChoice == 0   ? Mode::Engine
                                         : value->asChoice == 1 ? Mode::Off
                                                                : Mode::MaskOnly);
        SkyOverhaul::Clouds::SetEnabled(value->asChoice == 2);
    }

    void __cdecl OnSunChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Dazzle::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnNightChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Night::SetEnabled(value->asChoice == 1);
    }


    void __cdecl OnShadowsChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Shadows::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnGradeChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Grade::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnGrainChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Grain::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnOcclusionChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Occlusion::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnHazeChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Haze::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnFlattenChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Flatten::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnGrassChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Foliage::SetGrass(value->asChoice == 1);
    }

    void __cdecl OnLeavesChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Foliage::SetLeaves(value->asChoice == 1);
    }

    void __cdecl OnSunShadowsChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::SunShadows::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnWeatherChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Weather::SetEnabled(value->asChoice == 1);
        SkyOverhaul::Lightning::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnRainChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Rain::SetEnabled(value->asChoice == 1);
        SkyOverhaul::Ground::SetEnabled(value->asChoice == 1);
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

    // The effects draw into a frame the engine owns and hold objects on its device, so none is
    // installed without the seam that follows the frame and the one that lets go before a reset:
    // a plugin holding a render target through a reset would break the reset itself.
    if (SkyOverhaul::DeviceReset::Install(&OnDeviceRelease) &&
        SkyOverhaul::Frame::Install(&OnScenePass, &OnFinalPass,
                                    &SkyOverhaul::RainCover::OnOffscreenPass)) {
        SkyOverhaul::Clouds::Install();
        SkyOverhaul::RainComponent::Install();
        // Read by the wet ground, where its eight-bit steps drew lines across grass.
        SkyOverhaul::LinearDepth::Install();
        // Watches the draws inside a pass rather than the passes themselves, because that is where
        // the dome is and a sky has to go under everything drawn after it.
        SkyOverhaul::Sky::Install();
    }

    // The publisher every effect reads, which draws nothing: the sun, the moon and the weather as
    // the cloud layer is lit by them.
    SkyOverhaul::CloudLayer::Install();
    SkyOverhaul::SunShadows::Install();
    SkyOverhaul::Environment::Install();
    SkyOverhaul::Lightning::Install();

    // Before any engine code runs, so the first frame already draws with the stored values.
    SkyOverhaul::Tuning::Load();

    // Only which parts are on; every effect's values are tuned in bin\sky-overhaul.ini.
    // Each callback fires from inside RegisterSettings with what fcse.ini holds.
    static const FCSE_Setting settings[] = {
        {"Sky", FCSE_CHOICE(1), &OnSkyChanged, nullptr, kModes, std::size(kModes)},
        {"Clouds", FCSE_CHOICE(2), &OnCloudsChanged, nullptr, kCloudModes, std::size(kCloudModes)},
        {"Sun", FCSE_CHOICE(1), &OnSunChanged, nullptr, kModes, std::size(kModes)},
        {"Night", FCSE_CHOICE(1), &OnNightChanged, nullptr, kModes, std::size(kModes)},
        {"Shadows", FCSE_CHOICE(1), &OnShadowsChanged, nullptr, kModes, std::size(kModes)},
        {"Grade", FCSE_CHOICE(1), &OnGradeChanged, nullptr, kModes, std::size(kModes)},
        {"Grain", FCSE_CHOICE(1), &OnGrainChanged, nullptr, kSwitchModes, std::size(kSwitchModes)},
        {"Occlusion", FCSE_CHOICE(1), &OnOcclusionChanged, nullptr, kSwitchModes,
         std::size(kSwitchModes)},
        {"Heat haze", FCSE_CHOICE(1), &OnHazeChanged, nullptr, kSwitchModes,
         std::size(kSwitchModes)},
        {"Flatten", FCSE_CHOICE(1), &OnFlattenChanged, nullptr, kSwitchModes,
         std::size(kSwitchModes)},
        {"Grass", FCSE_CHOICE(1), &OnGrassChanged, nullptr, kModes, std::size(kModes)},
        {"Leaves", FCSE_CHOICE(1), &OnLeavesChanged, nullptr, kModes, std::size(kModes)},
        {"Sun shadows", FCSE_CHOICE(1), &OnSunShadowsChanged, nullptr, kModes, std::size(kModes)},
        {"Weather", FCSE_CHOICE(1), &OnWeatherChanged, nullptr, kModes, std::size(kModes)},
        {"Rain", FCSE_CHOICE(1), &OnRainChanged, nullptr, kModes, std::size(kModes)},
    };
    // Registered under the module name: the mod menu lists every loaded plugin and then every group
    // that matched none, so a group named apart from its DLL would arrive twice, once empty.
    api->RegisterSettings("SkyOverhaul", settings, std::size(settings));

    return true;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    DevTools::Overlay::AddWindow("Sky Overhaul", 480.0f, 720.0f, &SkyOverhaul::Tuning::DrawWindow);
}
