// Map Overhaul's plugin, for what the layer's data cannot do. See README.md.
#include "fcse_api.h"
#include "gps.h"
#include "level.h"
#include "markers.h"
#include "read.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    const bool gps = MapOverhaul::Gps::Install();
    const bool level = MapOverhaul::Level::Install();
    const bool markers = MapOverhaul::Markers::Install();
    // Reading close turns the sheet through the hold, so it waits on it.
    if (level) {
        MapOverhaul::Read::Install();
    }
    return gps || level || markers;
}
