// Inventory Overhaul - two guns of any class and a notebook for everything else. See README.md.
#include "two_guns.h"
#include "fcse_api.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    return InventoryOverhaul::TwoGuns::Install();
}
