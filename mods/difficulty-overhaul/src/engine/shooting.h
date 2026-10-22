// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#pragma once

#include "fcse_api.h"

#include <cstdint>
#include <unordered_map>

namespace DifficultyOverhaul::Shooting {

// A soldier's entry in a feature's map keyed by shooting system; past 512 soldiers it forgets them
// all, since dead soldiers are never erased.
template <typename T>
T& Remember(std::unordered_map<const void*, T>& soldiers, const void* shooting) {
    if (soldiers.size() > 512) {
        soldiers.clear();
    }
    return soldiers[shooting];
}

using CanShootFn = bool(__fastcall*)(void* task);

// CTaskShoot::CanShoot, whether he may pull the trigger now.
extern FCSE::Relocation<CanShootFn> CanShoot;

// Whether this build's shooting system and senses were found.
bool Found();

// The CPawnAgent he aims at, or null.
void* Target(void* shooting);

bool AimsAtPlayer(void* shooting);

// Whether a CPawnAgent is the player's.
bool IsPlayer(void* agent);

// How visible his target is to him now, 0 when he does not see it.
float Visibility(void* shooting);

// The distance to his target, as last measured.
float TargetDistance(const void* shooting);

// Whether his reaction timer runs, the seconds since his aim started settling, and restarting it.
bool Reacting(const void* shooting);
float AimSeconds(const void* shooting);
void RestartAim(void* shooting);

// The CPawn of a CPawnAgent, and of the soldier a shooting system belongs to.
void* Pawn(void* agent);
void* ShooterPawn(void* shooting);

// A CPawn's data block.
uint8_t* PawnData(void* pawn);

// The shooting system of a CPawnAgent, and of the soldier a CTaskShoot runs on.
void* OfAgent(void* agent);
void* OfTask(void* task);

}
