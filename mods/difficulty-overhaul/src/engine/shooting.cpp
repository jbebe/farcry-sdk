// A soldier's CShootingSystem and what it aims at: the seams every AI shooting feature reads.
#include "engine/shooting.h"

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

    // CShootingSystem's agent and target distance; CPawnAgent's IsAPlayer slot, its senses and
    // shooting system held in place, and its CPawn; CPawn's data block; CTaskShoot's agent.
    constexpr ptrdiff_t kShooter = 0x00;
    constexpr ptrdiff_t kTargetDistance = 0xE8;
    constexpr ptrdiff_t kIsAPlayerSlot = 0xBC;
    constexpr ptrdiff_t kAgentSenses = 0x28C;
    constexpr ptrdiff_t kAgentShooting = 0x4A0;
    constexpr ptrdiff_t kAgentPawn = 0x268;
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kTaskAgent = 0x34;
}

namespace DifficultyOverhaul::Shooting {

bool Found() { return g_getTarget && g_getVisibility; }

void* Target(void* shooting) { return g_getTarget(shooting); }

bool AimsAtPlayer(void* shooting) {
    void* target = Target(shooting);
    return target != nullptr &&
           Read<IsAPlayerFn>(Read<const void*>(target, 0), kIsAPlayerSlot)(target);
}

float Visibility(void* shooting) {
    auto* senses = Read<uint8_t*>(shooting, kShooter) + kAgentSenses;
    float visibility = 0.0f;
    g_getVisibility(senses, nullptr, Target(shooting), &visibility, false);
    return visibility;
}

float TargetDistance(const void* shooting) { return Read<float>(shooting, kTargetDistance); }

void* Pawn(void* agent) { return Read<void*>(agent, kAgentPawn); }

void* ShooterPawn(void* shooting) { return Pawn(Read<void*>(shooting, kShooter)); }

uint8_t* PawnData(void* pawn) { return Read<uint8_t*>(pawn, kPawnData); }

void* OfTask(void* task) { return Read<uint8_t*>(task, kTaskAgent) + kAgentShooting; }

}
