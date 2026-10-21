// The campaign state every feature reads: the difficulty, and which entity is the local player.
#pragma once

namespace DifficultyOverhaul::Game {

enum Difficulty { kUnknown = -1, kEasy, kNormal, kHardcore, kInfamous };

// The difficulty the campaign is played on, or kUnknown before the gameplay manager exists.
Difficulty CurrentDifficulty();

const char* Name(Difficulty difficulty);

// The local player's entity, or null outside a session.
void* PlayerEntity();

// The entity a CEntityProxy holds, or null.
void* ProxyEntity(const void* proxy);

}
