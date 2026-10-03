// Vehicle Overhaul - driving in Far Cry 2. See README.md.
#include "devtools_api.h"
#include "drivetrain.h"
#include "fcse_api.h"
#include "physics.h"
#include "sounds.h"
#include "third_person.h"
#include "tuning.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    VehicleOverhaul::Tuning::Load();

    // Any loaded part leaves hooks live, so the plugin stays as long as one did.
    const bool thirdPerson = VehicleOverhaul::ThirdPerson::Install();
    const bool physics = VehicleOverhaul::Physics::Install();
    const bool drivetrain = VehicleOverhaul::Drivetrain::Install();
    const bool sounds = VehicleOverhaul::Sounds::Install();
    return thirdPerson || physics || drivetrain || sounds;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    DevTools::Overlay::AddWindow("Vehicle Overhaul", 460.0f, 560.0f, &VehicleOverhaul::Tuning::DrawWindow);
}
