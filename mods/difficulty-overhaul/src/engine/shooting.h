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

// A soldier's visibility lines in his current state: where he notices a target, and where he
// recognises it. `agent` is his CPawnAgent.
float FuzzyLine(const void* agent);
float ClearLine(const void* agent);

// Whether a soldier is alert or fighting, by his CPawnAgent's state.
bool Alert(const void* agent);

// The line he fires at the player from: in a fight his fuzzy line, otherwise his clear one.
float FireLine(const void* agent);

// The soldier's CPawnAgent a CSensoryStimProcessor serves, or null when none or the player's.
void* ProcessorSoldier(const void* processor);

// The CPawnAgent a shooting system belongs to.
void* Agent(void* shooting);

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

// Whether his reaction timer runs, which stays stopped until his first shot in a fight, the seconds
// since his aim started settling, 0 while it is stopped, and restarting it.
bool Reacting(const void* shooting);
float AimSeconds(const void* shooting);
void RestartAim(void* shooting);

// His position and his target's, false when either has no entity.
bool Positions(void* shooting, float from[3], float to[3]);

// The CPawn of a CPawnAgent, and the CPawn of the soldier a shooting system belongs to.
void* Pawn(void* agent);
void* ShooterPawn(void* shooting);

// A CPawnAgent's entity id, as the logs name soldiers, and the id of a shooting system's soldier.
uint32_t Id(void* agent);
uint32_t ShooterId(void* shooting);

// CWeaponPropertiesCommon's selWeaponClass values.
enum WeaponClassId {
    kNoWeapon = -1, kMachete, kPistol, kAssault, kSMG, kShotgun, kSniper, kLMG, kLauncher
};

// Whether this build's weapon lookup was found; without it every soldier holds kNoWeapon.
bool WeaponClassFound();

// The class of the weapon in his hands, kNoWeapon when he holds none, as on a mounted gun.
int WeaponClass(void* shooting);

const char* WeaponClassName(int weaponClass);

// A CPawn's data block.
uint8_t* PawnData(void* pawn);

// A CPawn's stances: the two low ones are crouched and the one swimming forces.
enum StanceId { kStanding, kCrouched, kLow };

// A CPawn's velocity in metres per second, and its stance.
void Velocity(void* pawn, float velocity[3]);
int Stance(void* pawn);

const char* StanceName(int stance);

// The shooting system of a CPawnAgent, and of the soldier a CTaskShoot runs on.
void* OfAgent(void* agent);
void* OfTask(void* task);

}
