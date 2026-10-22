// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#include "engine/shooting.h"

#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

namespace {
    using GetTargetFn = void*(__fastcall*)(void* shooting);
    using IsAPlayerFn = bool(__fastcall*)(void* agent);
    using GetVisibilityFn = bool(__fastcall*)(void* senses, void* unused, void* agent,
                                              float* visibility, bool remembered);

    // CShootingSystem::GetTarget and CSensorySystem::GetVisibilityValue.
    FCSE::Relocation<GetTargetFn> g_getTarget{FCSE::Uplay(0x00A19370)};
    FCSE::Relocation<GetVisibilityFn> g_getVisibility{FCSE::Uplay(0x00A021A0)};

    // CShootingSystem's agent, its reaction CLifeTimer (lifetime, -1 while stopped, then start
    // time) and its target distance; CPawnAgent's IsAPlayer slot, its senses and shooting system
    // held in place, and its CPawn; CPawn's data block; CTaskShoot's agent.
    constexpr ptrdiff_t kShooter = 0x00;
    constexpr ptrdiff_t kReactionLifetime = 0xBC;
    constexpr ptrdiff_t kReactionStart = 0xC0;
    constexpr ptrdiff_t kTargetDistance = 0xE8;
    constexpr ptrdiff_t kIsAPlayerSlot = 0xBC;
    constexpr ptrdiff_t kAgentSenses = 0x28C;
    constexpr ptrdiff_t kAgentShooting = 0x4A0;
    constexpr ptrdiff_t kAgentPawn = 0x268;
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kTaskAgent = 0x34;
}

namespace DifficultyOverhaul::Shooting {

FCSE::Relocation<CanShootFn> CanShoot{FCSE::Uplay(0x009792F0)};

bool Found() { return g_getTarget && g_getVisibility; }

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

float TargetDistance(const void* shooting) { return Read<float>(shooting, kTargetDistance); }

bool Reacting(const void* shooting) { return Read<float>(shooting, kReactionLifetime) != -1.0f; }

float AimSeconds(const void* shooting) {
    return static_cast<float>(Game::Now()) - Read<float>(shooting, kReactionStart);
}

void RestartAim(void* shooting) {
    Write(shooting, kReactionStart, static_cast<float>(Game::Now()));
}

void* Pawn(void* agent) { return Read<void*>(agent, kAgentPawn); }

void* ShooterPawn(void* shooting) { return Pawn(Read<void*>(shooting, kShooter)); }

uint8_t* PawnData(void* pawn) { return Read<uint8_t*>(pawn, kPawnData); }

void* OfAgent(void* agent) { return static_cast<uint8_t*>(agent) + kAgentShooting; }

void* OfTask(void* task) { return OfAgent(Read<void*>(task, kTaskAgent)); }

}
