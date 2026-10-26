// Inventory Overhaul - two guns of any class and a notebook for everything else. See README.md.
#include "fcse_api.h"
#include "notebook.h"
#include "test_window.h"
#include "two_guns.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    const bool guns = InventoryOverhaul::TwoGuns::Install();
    const bool notebook = InventoryOverhaul::Notebook::Install();
    return guns || notebook;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    InventoryOverhaul::TestWindow::Add();
}
