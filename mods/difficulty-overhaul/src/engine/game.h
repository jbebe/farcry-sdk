// The campaign state every feature reads: the difficulty, the local player and the game clock.
#pragma once

namespace DifficultyOverhaul::Game {

enum Difficulty { kUnknown = -1, kEasy, kNormal, kHardcore, kInfamous };

// The difficulty the campaign is played on, or kUnknown before the gameplay manager exists.
Difficulty CurrentDifficulty();

const char* Name(Difficulty difficulty);

// The local player's entity, or null outside a session, and whether an entity is it.
void* PlayerEntity();
bool IsPlayerEntity(const void* entity);

// The entity a CEntityProxy holds, or null.
void* ProxyEntity(const void* proxy);

// An entity's world position.
void Position(const void* entity, float position[3]);

// Whether this build's game clock was found, and the clock in seconds.
bool ClockFound();
double Now();

}
