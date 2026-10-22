// Damage stims and the counters components they hit: the fields and handlers every damage feature
// reads.
#pragma once

#include "fcse_api.h"

#include <cstdint>

namespace DifficultyOverhaul::Damage {

// CEntityEventStims::selType of the stims a weapon deals.
enum StimType { kPierce = 3, kCrush = 4, kCut = 5 };

// The body parts the counters service keeps a damage multiplier for, in its order.
enum BodyPart { kHead, kTorso, kArms, kLegs, kHands, kFeet, kNoBodyPart };

// __thiscall with one stack argument, which a free function spells __fastcall.
using StimHandlerFn = void(__fastcall*)(void* counters, void* unused, const uint8_t* stim);

// CFCXCountersComponentPlayer::HandleDamageStim and CFCXCountersComponentAI::DamageHealth.
extern FCSE::Relocation<StimHandlerFn> PlayerDamage;
extern FCSE::Relocation<StimHandlerFn> SoldierDamage;

int Type(const uint8_t* stim);
int Level(const uint8_t* stim);
BodyPart Part(const uint8_t* stim);

// The entity that caused the stim, or null.
void* Instigator(const uint8_t* stim);

// Whether the local player caused the stim, and whether the player sent it: a near miss names
// the shooter only as its sender.
bool ByPlayer(const uint8_t* stim);
bool SentByPlayer(const uint8_t* stim);

// A CFCXCountersComponent's current health, or NaN.
float Health(const void* counters);

// The entity a counters component belongs to.
void* Owner(void* counters);

// The counters service's shared multiplier per BodyPart, kHead to kFeet, or null.
float* PartMultipliers(const void* counters);

}
