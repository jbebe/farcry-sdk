// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

void InstallHitLog();
void InstallDamageTaken();
void InstallDamageDealt();
void InstallHitModel();
void InstallIntuition();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    InstallHitLog();
    InstallDamageTaken();
    InstallDamageDealt();
    InstallHitModel();
    InstallIntuition();
    return true;
}
