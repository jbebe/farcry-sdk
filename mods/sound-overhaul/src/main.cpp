// Sound Overhaul - Far Cry 2's sound, closer to a serious shooter. See README.md.
#include "fcse_api.h"

// Lets DARE open a DirectSound whose device enumeration returns S_FALSE.
void ApplyEnumerationFix();
// Points Dunia.dll's DirectSound imports at the DSOAL shipped beside this plugin.
void LoadDsoal();
// CSoundSystem::PlaySoundReverb ships empty, so the game never changes DARE's reverb.
void ApplyReverbFix();
// Logs every reverb DARE switches to.
void InstallReverbLog();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false;
    }

    if (!FCSE::Bind(api)) {
        return false;
    }

    ApplyEnumerationFix();
    LoadDsoal();
    ApplyReverbFix();
    InstallReverbLog();
    return true;
}
