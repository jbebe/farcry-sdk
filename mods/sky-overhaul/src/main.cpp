// Sky Overhaul - the sun, as something you cannot look at.
//
// Nothing here changes how the sun itself looks. The glare is added over a frame the engine has
// already finished. See README.md for what is built and what is not.
#include "devtools_api.h"
#include "fcse_api.h"

#include "clouds.h"
#include "dazzle.h"
#include "engine/cloud_layer.h"
#include "engine/device_reset.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "sky.h"
#include "tuning.h"

#include <iterator>

namespace {
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

    // Index order is what the callbacks below switch on; fcse.ini stores the label.
    const char* const kModes[] = {"Engine", "Overhaul"};
    const char* const kCloudModes[] = {"Engine", "Off", "Overhaul"};

    void __cdecl OnSkyChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Sky::SetEnabled(value->asChoice == 1);
    }

    void __cdecl OnCloudsChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::CloudLayer::SetMode(value->asChoice == 0
                                             ? SkyOverhaul::CloudLayer::Mode::Engine
                                             : SkyOverhaul::CloudLayer::Mode::Off);
        SkyOverhaul::Clouds::SetEnabled(value->asChoice == 2);
    }

    void __cdecl OnSunChanged(const FCSE_SettingValue* value, void*) {
        SkyOverhaul::Dazzle::SetEnabled(value->asChoice == 1);
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

    // Before any engine code runs, so the first frame already draws with the stored values.
    SkyOverhaul::Tuning::Load();

    // Only which parts are on; the clouds' and the glare's values are tuned in bin\sky-overhaul.ini.
    // Each callback fires from inside RegisterSettings with what fcse.ini holds.
    static const FCSE_Setting settings[] = {
        {"Sky", FCSE_CHOICE(1), &OnSkyChanged, nullptr, kModes, std::size(kModes)},
        {"Clouds", FCSE_CHOICE(2), &OnCloudsChanged, nullptr, kCloudModes, std::size(kCloudModes)},
        {"Sun", FCSE_CHOICE(1), &OnSunChanged, nullptr, kModes, std::size(kModes)},
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
