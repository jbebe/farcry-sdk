// Hybrid processors: on a CPU with performance and efficiency cores, such as Intel's since Alder
// Lake, the game runs on the performance cores alone. It has no way to tell Windows which of its
// threads carry the frame, so one landing on a slower efficiency core holds the frame up.
// Elsewhere it keeps every processor. Either way this replaces whatever affinity it was started with.
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>
#include <vector>

namespace {
    // The cores of the highest efficiency class in processor group `group`, or 0 where the topology
    // cannot be read. Windows before 10 reports every core as class 0, so they all count.
    KAFFINITY PerformanceCores(WORD group) {
        DWORD length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, nullptr, &length);
        std::vector<uint8_t> buffer(length);
        if (length == 0 ||
            !GetLogicalProcessorInformationEx(
                RelationProcessorCore,
                reinterpret_cast<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*>(buffer.data()), &length)) {
            return 0;
        }

        BYTE fastest = 0;
        KAFFINITY cores = 0;
        for (DWORD offset = 0; offset < length;) {
            const auto* entry =
                reinterpret_cast<const SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*>(buffer.data() + offset);
            const PROCESSOR_RELATIONSHIP& core = entry->Processor;
            if (core.GroupMask[0].Group == group) {
                if (core.EfficiencyClass > fastest) {
                    fastest = core.EfficiencyClass;
                    cores = 0;
                }
                if (core.EfficiencyClass == fastest) {
                    cores |= core.GroupMask[0].Mask;
                }
            }
            offset += entry->Size;
        }
        return cores;
    }
}

void ApplyHybridCoresFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    DWORD_PTR processMask = 0;
    DWORD_PTR systemMask = 0;
    GROUP_AFFINITY thread = {};
    if (!GetProcessAffinityMask(GetCurrentProcess(), &processMask, &systemMask) ||
        !GetThreadGroupAffinity(GetCurrentThread(), &thread)) {
        api->Log("hybrid cores: the processors could not be read - affinity left untouched");
        return;
    }

    const DWORD_PTR performance = PerformanceCores(thread.Group) & systemMask;
    const DWORD_PTR wanted = performance != 0 ? performance : systemMask;
    if (!SetProcessAffinityMask(GetCurrentProcess(), wanted)) {
        FCSE::Logf("hybrid cores: affinity could not be set to 0x%08zX, error %lu",
                   static_cast<size_t>(wanted), GetLastError());
        return;
    }

    FCSE::Logf(wanted == systemMask ? "hybrid cores: none, so every processor, 0x%08zX"
                                    : "hybrid cores: the performance cores only, 0x%08zX",
               static_cast<size_t>(wanted));
}
