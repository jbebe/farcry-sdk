// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

void InstallHitLog();
void InstallDamageTaken();
void InstallDamageDealt();
void InstallHitModel();
void InstallFireDiscipline();
void InstallRangeFalloff();
void InstallIntuition();
void InstallGrass();
void InstallSuppression();
void InstallSquadMemory();
void InstallWatchedSpot();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    InstallHitLog();
    InstallDamageTaken();
    InstallDamageDealt();
    InstallHitModel();
    InstallFireDiscipline();
    InstallRangeFalloff();
    InstallIntuition();
    InstallGrass();
    InstallSuppression();
    InstallSquadMemory();
    InstallWatchedSpot();
    return true;
}
