// Vehicle Overhaul - driving in Far Cry 2. See README.md.
#include "fcse_api.h"
#include "third_person.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    return api->apiVersion == FCSE_API_VERSION && FCSE::Bind(api) && VehicleOverhaul::ThirdPerson::Install();
}
