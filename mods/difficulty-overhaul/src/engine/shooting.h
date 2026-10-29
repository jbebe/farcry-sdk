// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#pragma once

#include "fcse_api.h"

#include <cstdint>

namespace DifficultyOverhaul::Shooting {

using TaskCheckFn = bool(__fastcall*)(void* task);

// CTaskShoot::CanShoot, whether he may pull the trigger now; refused, the task ends.
extern FCSE::Relocation<TaskCheckFn> CanShoot;

// CTaskShoot::IsAligned, whether his aim is on the target; until it is, the task waits to fire.
extern FCSE::Relocation<TaskCheckFn> IsAligned;

// Whether this build's shooting system and senses were found.
bool Found();

// The CPawnAgent he aims at, or null.
void* Target(void* shooting);

bool AimsAtPlayer(void* shooting);

// Whether a CPawnAgent is the player's.
bool IsPlayer(void* agent);

// How visible his target is to him now, 0 when he does not see it.
float Visibility(void* shooting);

// The visibility at which he makes out a target at all, in his current state.
float FuzzyVisibility(void* shooting);

// Shakes his aim: for `seconds` his chance at the player takes `factor`, a panic if `panic`. A worse
// shake that still holds is kept. Returns whether any held.
bool Shake(const void* shooting, float factor, bool panic, double seconds);

// The factor his chance takes from a shake, 1 when none holds, and whether it is a panic.
float Shaken(const void* shooting);
bool Panicking(const void* shooting);

// How long he has had his target in his sight without a break, or -1 when it is out of sight.
float SightSeconds(void* shooting);

// The distance to his target, as last measured.
float TargetDistance(const void* shooting);

// Whether his reaction timer runs, the seconds since his aim started settling, and restarting it.
bool Reacting(const void* shooting);
float AimSeconds(const void* shooting);
void RestartAim(void* shooting);

// The CPawn of a CPawnAgent, and the CPawn and entity of the soldier a shooting system belongs to.
void* Pawn(void* agent);
void* ShooterPawn(void* shooting);
void* ShooterEntity(void* shooting);

// A CPawn's data block.
uint8_t* PawnData(void* pawn);

// A CPawn's velocity in metres per second, and its stance, 1 when crouched.
void Velocity(void* pawn, float velocity[3]);
int Stance(void* pawn);

// The shooting system of a CPawnAgent, and of the soldier a CTaskShoot runs on.
void* OfAgent(void* agent);
void* OfTask(void* task);

}
