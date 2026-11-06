// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#include "engine/shooting.h"

#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <unordered_map>

namespace {
    using GetTargetFn = void*(__fastcall*)(void* shooting);
    using IsAPlayerFn = bool(__fastcall*)(void* agent);
    using GetVisibilityFn = bool(__fastcall*)(void* senses, void* unused, void* agent,
                                              float* visibility, bool remembered);
    using GetKnowledgeFn = bool(__fastcall*)(void* knowledge, void* unused, const uint32_t* id,
                                             uint8_t** entry);
    using GetEquippedWeaponFn = void*(__fastcall*)(void* inventory);

    // CShootingSystem::GetTarget, CSensorySystem::GetVisibilityValue,
    // CSensorySystemKB::GetKBEntryForEntity and CInventoryViewPawn::GetEquippedWeapon.
    FCSE::Relocation<GetTargetFn> g_getTarget{FCSE::Uplay(0x00A19370)};
    FCSE::Relocation<GetVisibilityFn> g_getVisibility{FCSE::Uplay(0x00A021A0)};
    FCSE::Relocation<GetKnowledgeFn> g_getKnowledge{FCSE::Uplay(0x00A235C0)};
    FCSE::Relocation<GetEquippedWeaponFn> g_getEquippedWeapon{FCSE::Uplay(0x00127DA0)};

    // CShootingSystem: its agent, its reaction CLifeTimer's lifetime (-1 while stopped) and start time,
    // and its target distance.
    constexpr ptrdiff_t kShooter = 0x00;
    constexpr ptrdiff_t kReactionLifetime = 0xBC;
    constexpr ptrdiff_t kReactionStart = 0xC0;
    constexpr ptrdiff_t kTargetDistance = 0xE8;
    // CPawnAgent: its id, IsAPlayer slot and CPawn, and its senses and shooting system held in place.
    constexpr ptrdiff_t kAgentId = 0x0C;
    constexpr ptrdiff_t kIsAPlayerSlot = 0xBC;
    constexpr ptrdiff_t kAgentPawn = 0x268;
    constexpr ptrdiff_t kAgentSenses = 0x28C;
    constexpr ptrdiff_t kAgentShooting = 0x4A0;
    // CPawnAgent's visibility lines for its current state, fuzzy then clear.
    constexpr ptrdiff_t kAgentFuzzy = 0x868;
    constexpr ptrdiff_t kAgentClear = 0x86C;
    // CPawnAgent's state: 2 alert, 3 to 5 the combat states.
    constexpr ptrdiff_t kAgentState = 0x274;
    constexpr int kAlertState = 2;
    constexpr int kLastCombatState = 5;
    // CSensoryStimProcessor's agent.
    constexpr ptrdiff_t kProcessorAgent = 0x04;
    // CSensorySystem's knowledge base, and a knowledge entry's unbroken seconds in the focus and
    // the peripheral cone, -1 while out of it.
    constexpr ptrdiff_t kSensesKnowledge = 0x24;
    constexpr ptrdiff_t kEntryFocusSeconds = 0x0C;
    constexpr ptrdiff_t kEntryPeripheralSeconds = 0x10;
    // CPawn's data block, and the stance and velocity in its effective data.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataEffective = 0x2D0;
    constexpr ptrdiff_t kEffectiveStance = 0xA4;
    constexpr ptrdiff_t kEffectiveVelocity = 0xB0;
    // The inventory view in a CPawn's data block; CWeapon's properties holder, the
    // CWeaponPropertiesCommon in it, and its selWeaponClass.
    constexpr ptrdiff_t kDataInventory = 0x4F0;
    constexpr ptrdiff_t kWeaponPropertiesHolder = 0x24;
    constexpr ptrdiff_t kHolderProperties = 0x04;
    constexpr ptrdiff_t kPropertiesClass = 0x40;
    // CTaskShoot's agent.
    constexpr ptrdiff_t kTaskAgent = 0x34;

    const char* const kWeaponClassNames[] = {"machete", "pistol",       "assault rifle", "SMG",
                                             "shotgun", "sniper rifle", "machine gun",   "launcher"};
    const char* const kStanceNames[] = {"standing", "crouched", "swimming"};

    // The last shake of each shooting system, and until when it holds by game clock.
    struct Mark {
        double until = -1.0;
        float factor = 1.0f;
        bool panic = false;
    };
    std::unordered_map<const void*, Mark> g_marks;
}

namespace DifficultyOverhaul::Shooting {

FCSE::Relocation<TaskCheckFn> CanShoot{FCSE::Uplay(0x009792F0)};
FCSE::Relocation<TaskCheckFn> IsAligned{FCSE::Uplay(0x0097A3D0)};

bool Found() { return g_getTarget && g_getVisibility && g_getKnowledge; }

bool WeaponClassFound() { return static_cast<bool>(g_getEquippedWeapon); }

void* Target(void* shooting) { return g_getTarget(shooting); }

bool AimsAtPlayer(void* shooting) {
    void* target = Target(shooting);
    return target != nullptr && IsPlayer(target);
}

bool IsPlayer(void* agent) {
    return Read<IsAPlayerFn>(Read<const void*>(agent, 0), kIsAPlayerSlot)(agent);
}

float Visibility(void* shooting) {
    auto* senses = Read<uint8_t*>(shooting, kShooter) + kAgentSenses;
    float visibility = 0.0f;
    g_getVisibility(senses, nullptr, Target(shooting), &visibility, false);
    return visibility;
}

float FuzzyLine(const void* agent) { return Read<float>(agent, kAgentFuzzy); }

float ClearLine(const void* agent) { return Read<float>(agent, kAgentClear); }

bool Alert(const void* agent) {
    const int state = Read<int>(agent, kAgentState);
    return state >= kAlertState && state <= kLastCombatState;
}

float FireLine(const void* agent) {
    const int state = Read<int>(agent, kAgentState);
    return state > kAlertState && state <= kLastCombatState ? FuzzyLine(agent) : ClearLine(agent);
}

void* ProcessorSoldier(const void* processor) {
    void* agent = Read<void*>(processor, kProcessorAgent);
    return agent == nullptr || IsPlayer(agent) ? nullptr : agent;
}

void* Agent(void* shooting) { return Read<void*>(shooting, kShooter); }

namespace {
    // The shake that holds on him now, if any.
    const Mark* Holding(const void* shooting) {
        const auto shake = g_marks.find(shooting);
        return shake != g_marks.end() && Game::Now() < shake->second.until ? &shake->second
                                                                             : nullptr;
    }
}

bool Shake(const void* shooting, float factor, bool panic, double seconds) {
    const Mark* holding = Holding(shooting);
    if (holding == nullptr || holding->factor >= factor) {
        Remember(g_marks, shooting) = {Game::Now() + seconds, factor, panic};
    }
    return holding != nullptr;
}

float Shaken(const void* shooting) {
    const Mark* holding = Holding(shooting);
    return holding != nullptr ? holding->factor : 1.0f;
}

bool Panicking(const void* shooting) {
    const Mark* holding = Holding(shooting);
    return holding != nullptr && holding->panic;
}

namespace {
    // A CPawnAgent's knowledge entry for another agent, or null when he knows nothing of it.
    const uint8_t* Knowledge(const void* agent, const void* target) {
        const auto* senses = static_cast<const uint8_t*>(agent) + kAgentSenses;
        uint8_t* entry = nullptr;
        if (target == nullptr ||
            !g_getKnowledge(Read<void*>(senses, kSensesKnowledge), nullptr,
                            reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(target) + kAgentId),
                            &entry)) {
            return nullptr;
        }
        return entry;
    }
}

float SightSeconds(void* shooting) {
    const uint8_t* entry = Knowledge(Read<const void*>(shooting, kShooter), Target(shooting));
    if (entry == nullptr) {
        return -1.0f;
    }
    return std::max(Read<float>(entry, kEntryFocusSeconds),
                    Read<float>(entry, kEntryPeripheralSeconds));
}

float TargetDistance(const void* shooting) { return Read<float>(shooting, kTargetDistance); }

bool Reacting(const void* shooting) { return Read<float>(shooting, kReactionLifetime) != -1.0f; }

float AimSeconds(const void* shooting) {
    return Reacting(shooting) ? static_cast<float>(Game::Now()) - Read<float>(shooting, kReactionStart) : 0.0f;
}

void RestartAim(void* shooting) {
    Write(shooting, kReactionStart, static_cast<float>(Game::Now()));
}

bool Positions(void* shooting, float from[3], float to[3]) {
    void* shooter = Game::EntityOf(ShooterPawn(shooting));
    void* target = Game::EntityOf(Pawn(Target(shooting)));
    if (shooter == nullptr || target == nullptr) {
        return false;
    }
    Game::Position(shooter, from);
    Game::Position(target, to);
    return true;
}

void* Pawn(void* agent) { return Read<void*>(agent, kAgentPawn); }

void* ShooterPawn(void* shooting) { return Pawn(Agent(shooting)); }

uint32_t Id(void* agent) { return Game::Id(Game::EntityOf(Pawn(agent))); }

uint32_t ShooterId(void* shooting) { return Id(Agent(shooting)); }

int WeaponClass(void* shooting) {
    if (!g_getEquippedWeapon) {
        return kNoWeapon;
    }
    void* weapon = g_getEquippedWeapon(PawnData(ShooterPawn(shooting)) + kDataInventory);
    if (weapon == nullptr) {
        return kNoWeapon;
    }
    const void* holder = Read<void*>(weapon, kWeaponPropertiesHolder);
    return Read<int>(Read<void*>(holder, kHolderProperties), kPropertiesClass);
}

const char* WeaponClassName(int weaponClass) {
    if (weaponClass == kNoWeapon) {
        return "no weapon in hand";
    }
    return weaponClass >= kMachete && weaponClass <= kLauncher ? kWeaponClassNames[weaponClass]
                                                                 : "weapon of another class";
}

uint8_t* PawnData(void* pawn) { return Read<uint8_t*>(pawn, kPawnData); }

void Velocity(void* pawn, float velocity[3]) {
    std::copy_n(Vec3(PawnData(pawn) + kDataEffective, kEffectiveVelocity), 3, velocity);
}

int Stance(void* pawn) { return Read<int>(PawnData(pawn) + kDataEffective, kEffectiveStance); }

const char* StanceName(int stance) {
    return stance >= kStanding && stance <= kLow ? kStanceNames[stance] : "in another stance";
}

void* OfAgent(void* agent) { return static_cast<uint8_t*>(agent) + kAgentShooting; }

void* OfTask(void* task) { return OfAgent(Read<void*>(task, kTaskAgent)); }

}
