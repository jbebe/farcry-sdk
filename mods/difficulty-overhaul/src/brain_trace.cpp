// Brain trace: with -braintrace on the command line, logs each task a soldier's combat brain starts,
// by the CRC32 of its instance name in mercbrain.
#include "engine/combat_log.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>
#include <cstring>

using namespace DifficultyOverhaul;

namespace {
    using StartFn = void(__fastcall*)(void* task);

    // CTask::Start.
    FCSE::Relocation<StartFn> g_start{FCSE::Uplay(0x0031CA80)};
    StartFn g_originalStart = nullptr;

    constexpr char kTraceSwitch[] = "-braintrace";
    // CTask: its name's CRC32, the plan it runs in, and its agent.
    constexpr ptrdiff_t kTaskName = 0x04;
    constexpr ptrdiff_t kTaskPlan = 0x08;
    constexpr ptrdiff_t kTaskAgent = 0x0C;
    // CRC32 of "::MercBrain/MercBrain/MercBrainCombat".
    constexpr uint32_t kCombatBrain = 0xB67506ED;

    bool InCombatBrain(const void* task) {
        for (; task != nullptr; task = Read<const void*>(task, kTaskPlan)) {
            if (Read<uint32_t>(task, kTaskName) == kCombatBrain) {
                return true;
            }
        }
        return false;
    }

    void __fastcall StartDetour(void* task) {
        void* agent = Read<void*>(task, kTaskAgent);
        if (agent != nullptr && InCombatBrain(task)) {
            CombatLogf("soldier %08X brain: %08X", Shooting::Id(agent), Read<uint32_t>(task, kTaskName));
        }
        g_originalStart(task);
    }
}

void InstallBrainTrace() {
    if (std::strstr(GetCommandLineA(), kTraceSwitch) == nullptr) {
        return;
    }
    if (!Hook(g_start, &StartDetour, &g_originalStart)) {
        FCSE::Logf("brain trace: the brain's task start was not found in this build");
    }
}
