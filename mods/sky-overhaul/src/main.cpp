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

    // Before any engine code runs, so the sky is in the state it was left in from the first frame.
    SkyOverhaul::Tuning::Load();

    return true;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    DevTools::Overlay::AddWindow("Sky Overhaul", 480.0f, 720.0f, &SkyOverhaul::Tuning::DrawWindow);
}
