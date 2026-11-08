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
void InstallSquadRoles();
void InstallInTheOpen();
void InstallWatchedSpot();
void InstallPerception();
void InstallGoingDown();
void InstallHealing();
void InstallBrainTrace();
void InstallBloodSpatter();
void InstallBloodPuddle();

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
    InstallSquadRoles();
    InstallInTheOpen();
    InstallWatchedSpot();
    InstallPerception();
    InstallGoingDown();
    InstallHealing();
    InstallBrainTrace();
    InstallBloodSpatter();
    InstallBloodPuddle();
    return true;
}
