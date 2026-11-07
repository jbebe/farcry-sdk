// Map Overhaul's plugin, for what the layer's data cannot do. See README.md.
#include "fcse_api.h"
#include "gps.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    return api->apiVersion == FCSE_API_VERSION && FCSE::Bind(api) && MapOverhaul::Gps::Install();
}
