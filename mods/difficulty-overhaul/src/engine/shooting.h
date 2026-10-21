// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#pragma once

#include <cstdint>

namespace DifficultyOverhaul::Shooting {

// Whether this build's shooting system and senses were found.
bool Found();

// The CPawnAgent he aims at, or null.
void* Target(void* shooting);

bool AimsAtPlayer(void* shooting);

// How visible his target is to him now, 0 when he does not see it.
float Visibility(void* shooting);

// The distance to his target, as last measured.
float TargetDistance(const void* shooting);

// The CPawn of a CPawnAgent, and of the soldier a shooting system belongs to.
void* Pawn(void* agent);
void* ShooterPawn(void* shooting);

// A CPawn's data block.
uint8_t* PawnData(void* pawn);

// The shooting system of the soldier a CTaskShoot runs on.
void* OfTask(void* task);

}
