// CPU from GetProcessTimes and GetThreadTimes deltas, the address space from a VirtualQuery walk, and
// the GPU from the same performance counters Task Manager reads. Only the sampler thread touches
// anything in this file other than the published snapshot.
#include "engine/process_stats.h"

#include "engine/game_thread.h"
#include "fcse_api.h"

#include <algorithm>
#include <mutex>
#include <thread>
#include <windows.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <psapi.h>

namespace {
    using DevTools::ProcessStats::Snapshot;

    std::mutex g_lock;
    Snapshot g_snapshot;

    uint64_t g_lastWall = 0;
    uint64_t g_lastProcess = 0;
    uint64_t g_lastThread = 0;
    HANDLE g_engineThread = nullptr;
    SYSTEM_INFO g_system{};

    PDH_HQUERY g_query = nullptr;
    PDH_HCOUNTER g_gpuUsage = nullptr;
    PDH_HCOUNTER g_dedicated = nullptr;
    PDH_HCOUNTER g_shared = nullptr;

    uint64_t Ticks(const FILETIME& time) {
        return (static_cast<uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
    }

    uint64_t Wall() {
        FILETIME now{};
        GetSystemTimePreciseAsFileTime(&now);
        return Ticks(now);
    }

    uint64_t ProcessTime() {
        FILETIME created{}, exited{}, kernel{}, user{};
        GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user);
        return Ticks(kernel) + Ticks(user);
    }

    uint64_t ThreadTime(HANDLE thread) {
        FILETIME created{}, exited{}, kernel{}, user{};
        GetThreadTimes(thread, &created, &exited, &kernel, &user);
        return Ticks(kernel) + Ticks(user);
    }

    void SampleCpu(Snapshot& snapshot) {
        const uint64_t wall = Wall();
        const uint64_t process = ProcessTime();
        const double elapsed = static_cast<double>(wall - g_lastWall);

        snapshot.processCores = static_cast<float>((process - g_lastProcess) / elapsed);
        snapshot.processCpu = snapshot.processCores * 100.0f / g_system.dwNumberOfProcessors;

        if (g_engineThread != nullptr) {
            const uint64_t thread = ThreadTime(g_engineThread);
            snapshot.engineThread = static_cast<float>((thread - g_lastThread) * 100.0 / elapsed);
            g_lastThread = thread;
        } else if (const unsigned long id = DevTools::GameThread::Id(); id != 0) {
            g_engineThread = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, FALSE, id);
            if (g_engineThread != nullptr) {
                g_lastThread = ThreadTime(g_engineThread);
            }
        }

        g_lastWall = wall;
        g_lastProcess = process;
    }

    void SampleAddressSpace(Snapshot& snapshot) {
        uint64_t address = reinterpret_cast<uintptr_t>(g_system.lpMinimumApplicationAddress);
        const uint64_t end = reinterpret_cast<uintptr_t>(g_system.lpMaximumApplicationAddress) + 1;
        snapshot.addressLimit = end;

        MEMORY_BASIC_INFORMATION region{};
        while (address < end &&
               VirtualQuery(reinterpret_cast<void*>(static_cast<uintptr_t>(address)), &region,
                            sizeof(region)) != 0) {
            const uint64_t size = region.RegionSize;
            if (region.State == MEM_COMMIT) {
                snapshot.committed += size;
            } else if (region.State == MEM_RESERVE) {
                snapshot.reserved += size;
            } else {
                snapshot.largestFree = std::max(snapshot.largestFree, size);
            }
            address = reinterpret_cast<uintptr_t>(region.BaseAddress) + size;
        }

        PROCESS_MEMORY_COUNTERS_EX counters{};
        if (GetProcessMemoryInfo(GetCurrentProcess(),
                                 reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
                                 sizeof(counters))) {
            snapshot.privateBytes = counters.PrivateUsage;
            snapshot.workingSet = counters.WorkingSetSize;
        }
    }

    // Opened once the engine is running, so the device and its GPU counters exist to be matched.
    void OpenGpuCounters() {
        if (PdhOpenQueryW(nullptr, 0, &g_query) != ERROR_SUCCESS) {
            FCSE::ApiPointer()->Log("process stats: no performance counters - GPU usage and VRAM "
                                    "are not read");
            return;
        }

        const unsigned long pid = GetCurrentProcessId();
        wchar_t usage[128], dedicated[128], shared[128];
        swprintf_s(usage, L"\\GPU Engine(pid_%lu_*engtype_3D)\\Utilization Percentage", pid);
        swprintf_s(dedicated, L"\\GPU Process Memory(pid_%lu_*)\\Dedicated Usage", pid);
        swprintf_s(shared, L"\\GPU Process Memory(pid_%lu_*)\\Shared Usage", pid);

        if (PdhAddEnglishCounterW(g_query, usage, 0, &g_gpuUsage) != ERROR_SUCCESS ||
            PdhAddEnglishCounterW(g_query, dedicated, 0, &g_dedicated) != ERROR_SUCCESS ||
            PdhAddEnglishCounterW(g_query, shared, 0, &g_shared) != ERROR_SUCCESS) {
            PdhCloseQuery(g_query);
            g_query = nullptr;
            FCSE::ApiPointer()->Log("process stats: this Windows has no GPU counters - GPU usage "
                                    "and VRAM are not read");
            return;
        }

        // A rate needs two samples; this is the first.
        PdhCollectQueryData(g_query);
        FCSE::ApiPointer()->Log("process stats: reading GPU usage and VRAM from the GPU counters");
    }

    // Sums every instance the wildcard matched. False while none has a value.
    bool Sum(PDH_HCOUNTER counter, double& total) {
        DWORD bytes = 0;
        DWORD count = 0;
        if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bytes, &count, nullptr) !=
            PDH_MORE_DATA) {
            return false;
        }

        std::vector<BYTE> buffer(bytes);
        auto* items = reinterpret_cast<PDH_FMT_COUNTERVALUE_ITEM_W*>(buffer.data());
        if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, &bytes, &count, items) !=
            ERROR_SUCCESS) {
            return false;
        }

        bool any = false;
        total = 0.0;
        for (DWORD index = 0; index < count; ++index) {
            const PDH_FMT_COUNTERVALUE& value = items[index].FmtValue;
            if (value.CStatus == PDH_CSTATUS_VALID_DATA || value.CStatus == PDH_CSTATUS_NEW_DATA) {
                total += value.doubleValue;
                any = true;
            }
        }
        return any;
    }

    void SampleGpu(Snapshot& snapshot) {
        if (g_query == nullptr || PdhCollectQueryData(g_query) != ERROR_SUCCESS) {
            return;
        }

        double usage = 0.0;
        double dedicated = 0.0;
        double shared = 0.0;
        snapshot.gpuKnown = Sum(g_gpuUsage, usage) && Sum(g_dedicated, dedicated);
        Sum(g_shared, shared);
        snapshot.gpuUsage = static_cast<float>(std::min(usage, 100.0));
        snapshot.vramDedicated = static_cast<uint64_t>(dedicated);
        snapshot.vramShared = static_cast<uint64_t>(shared);
    }

    void Run() {
        bool gpuOpened = false;
        for (;;) {
            Sleep(1000);

            if (!gpuOpened && DevTools::GameThread::Id() != 0) {
                gpuOpened = true;
                OpenGpuCounters();
            }

            Snapshot snapshot;
            SampleCpu(snapshot);
            SampleAddressSpace(snapshot);
            SampleGpu(snapshot);

            std::lock_guard<std::mutex> held(g_lock);
            snapshot.serial = g_snapshot.serial + 1;
            snapshot.peakCommitted = std::max(g_snapshot.peakCommitted, snapshot.committed);
            snapshot.peakPrivate = std::max(g_snapshot.peakPrivate, snapshot.privateBytes);
            snapshot.lowestLargestFree =
                std::min(g_snapshot.lowestLargestFree, snapshot.largestFree);
            g_snapshot = snapshot;
        }
    }
}

namespace DevTools::ProcessStats {

void Start() {
    GetSystemInfo(&g_system);
    g_lastWall = Wall();
    g_lastProcess = ProcessTime();
    std::thread(&Run).detach();
    FCSE::ApiPointer()->Log("process stats: sampling CPU and memory once a second");
}

Snapshot Read() {
    std::lock_guard<std::mutex> held(g_lock);
    return g_snapshot;
}

}
