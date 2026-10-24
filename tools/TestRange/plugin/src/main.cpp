// The test range's scenarios, as a window in DevTools' overlay. See ../../README.md.
#include "devtools_api.h"
#include "fcse_api.h"

#include "window.h"

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    return api->apiVersion == FCSE_API_VERSION && FCSE::Bind(api);
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    DevTools::Overlay::AddWindow("Test range", 440.0f, 620.0f, &TestRange::Window::Draw);
}
