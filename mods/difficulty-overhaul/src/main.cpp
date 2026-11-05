// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

void InstallHitLog();
void InstallDamageTaken();
void InstallHitModel();
void InstallFireDiscipline();
void InstallRangeFalloff();
void InstallIntuition();
void InstallSuppression();
void InstallSquadMemory();
void InstallWatchedSpot();
void InstallPerception();
void InstallGoingDown();
void InstallRegeneration();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    InstallHitLog();
    InstallDamageTaken();
    InstallHitModel();
    InstallFireDiscipline();
    InstallRangeFalloff();
    InstallIntuition();
    InstallSuppression();
    InstallSquadMemory();
    InstallWatchedSpot();
    InstallPerception();
    InstallGoingDown();
    InstallRegeneration();
    return true;
}
