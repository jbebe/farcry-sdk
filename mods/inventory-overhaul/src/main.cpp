// Inventory Overhaul - two guns of any class and a notebook for everything else. See README.md.
#include "engine/game_thread.h"
#include "engine/paper.h"
#include "fcse_api.h"
#include "notebook.h"
#include "page.h"
#include "test_window.h"
#include "two_guns.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    using namespace InventoryOverhaul;
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    const bool guns = TwoGuns::Install();
    const bool notebook = Notebook::Install() && Page::Install() && GameThread::Install();
    if (notebook) {
        GameThread::EachFrame(&Page::Frame);
        Paper::Install();
    }
    return guns || notebook;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    InventoryOverhaul::TestWindow::Add();
}
