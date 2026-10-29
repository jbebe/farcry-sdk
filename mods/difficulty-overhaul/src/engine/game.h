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

// The entity a component such as a CPawn or a counters component belongs to, or null.
void* EntityOf(void* component);

// An entity's world position, and the distance between two positions.
void Position(const void* entity, float position[3]);
float Distance(const float a[3], const float b[3]);

// Whether this build's game clock was found, and the clock in seconds.
bool ClockFound();
double Now();

}
