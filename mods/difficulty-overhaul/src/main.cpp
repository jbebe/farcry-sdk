// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

void InstallDamageTaken();
void InstallHitModel();
void InstallFireDiscipline();
void InstallRangeFalloff();
void InstallIntuition();
void InstallSuppression();
void InstallSquadMemory();
void InstallSquadRoles();
void InstallInTheOpen();
void InstallGrenades();
void InstallWatchedSpot();
void InstallPerception();
void InstallFlashlight();
void InstallGoingDown();
void InstallHealing();
void InstallBrainTrace();
void InstallBloodSpatter();
void InstallBloodPuddle();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    InstallDamageTaken();
    InstallHitModel();
    InstallFireDiscipline();
    InstallRangeFalloff();
    InstallIntuition();
    InstallSuppression();
    InstallSquadMemory();
    InstallSquadRoles();
    InstallInTheOpen();
    InstallGrenades();
    InstallWatchedSpot();
    InstallPerception();
    InstallFlashlight();
    InstallGoingDown();
    InstallHealing();
    InstallBrainTrace();
    InstallBloodSpatter();
    InstallBloodPuddle();
    return true;
}
