// Sound Overhaul - Far Cry 2's sound, closer to a serious shooter. See README.md.
#include "devtools_api.h"
#include "fcse_api.h"

#include "mutes.h"

// Lets DARE open a DirectSound whose device enumeration returns S_FALSE.
void ApplyEnumerationFix();
// Points Dunia.dll's DirectSound imports at the DSOAL shipped beside this plugin.
void LoadDsoal();
// CSoundSystem::PlaySoundReverb ships empty, so the game never changes DARE's reverb.
void ApplyReverbFix();
// Logs every reverb DARE switches to.
void InstallReverbLog();
// Logs every echo length a gunshot echo is trimmed to.
void InstallEchoLog();
// Raises the reverb send of 2D voices, the player's own sounds among them.
void ApplyPlayerReverb();
// Plays a loop-less full-auto weapon's single shot on every round.
void ApplyPerRoundShots();
// Gives NPC shots an echo, and lets only the last echo of a burst ring out.
void ApplyLastEcho();
// Gives every building a reverb by its type and size, and a muffle by its material.
void ApplyRooms();

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
    InstallEchoLog();
    ApplyPlayerReverb();
    ApplyPerRoundShots();
    ApplyLastEcho();
    ApplyRooms();
    SoundOverhaul::Mutes::Install();
    return true;
}

// Runs after every plugin's FCSE_Load, so DevTools has loaded by now if it is installed at all.
extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI*) {
    DevTools::Overlay::AddWindow("Sound Overhaul", 340.0f, 150.0f, &SoundOverhaul::Mutes::DrawWindow);
}
