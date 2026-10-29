// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

void InstallHitLog();
void InstallDamageTaken();
void InstallDamageDealt();
void InstallHitModel();
void InstallFireDiscipline();
void InstallReaction();
void InstallRangeFalloff();
void InstallIntuition();
void InstallGrass();
void InstallSuppression();
void InstallSquadMemory();
void InstallWatchedSpot();
void InstallEyesight();
void InstallGoingDown();
void InstallHealing();

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    InstallHitLog();
    InstallDamageTaken();
    InstallDamageDealt();
    InstallHitModel();
    InstallFireDiscipline();
    // After fire discipline, so its hook wraps fire discipline's: a hold starts only once he
    // would fire.
    InstallReaction();
    InstallRangeFalloff();
    InstallIntuition();
    InstallGrass();
    InstallSuppression();
    InstallSquadMemory();
    InstallWatchedSpot();
    InstallEyesight();
    InstallGoingDown();
    InstallHealing();
    return true;
}
