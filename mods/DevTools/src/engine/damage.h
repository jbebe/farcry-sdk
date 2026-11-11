// Damage stims, and the handlers of the counters components they hit.
#pragma once

#include "fcse_api.h"

#include <cstdint>

namespace DevTools::Damage {

// CEntityEventStims::selType of the stims a weapon deals.
enum StimType { kPierce = 3, kCrush = 4, kCut = 5 };

enum BodyPart { kHead, kTorso, kArms, kLegs, kHands, kFeet, kNoBodyPart };

// __thiscall with one stack argument, which a free function spells __fastcall.
using StimHandlerFn = void(__fastcall*)(void* counters, void* unused, const uint8_t* stim);

// CFCXCountersComponentPlayer::HandleDamageStim and CFCXCountersComponentAI::DamageHealth.
extern FCSE::Relocation<StimHandlerFn> PlayerDamage;
extern FCSE::Relocation<StimHandlerFn> SoldierDamage;

int Type(const uint8_t* stim);
int Level(const uint8_t* stim);
BodyPart Part(const uint8_t* stim);

// The entity that caused the stim, or null, and whether it is the local player.
void* Instigator(const uint8_t* stim);
bool ByPlayer(const uint8_t* stim);

}
