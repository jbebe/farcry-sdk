// Sound Overhaul - Far Cry 2's sound, closer to a serious shooter. See README.md.
#include "fcse_api.h"

// DARE rejects a DirectSound whose device enumeration returns S_FALSE, and shows no sound at all.
void ApplyEnumerationFix();
// Points Dunia.dll's DirectSound imports at the DSOAL shipped beside this plugin.
void LoadDsoal();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false;
    }

    if (!FCSE::Bind(api)) {
        return false;
    }

    // FCSE_Load runs before any Dunia.dll engine code, so both are in place before DARE opens its
    // device.
    ApplyEnumerationFix();
    LoadDsoal();
    return true;
}
