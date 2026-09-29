// The process as Windows accounts for it - CPU time, address space, memory and GPU - sampled once a
// second on a thread of its own, so none of it is paid for by the frames being measured.
#pragma once

#include <cstdint>
#include <limits>
#include <string>

namespace DevTools::ProcessStats {

struct Snapshot {
    // Counts samples; 0 until the first one lands.
    unsigned serial = 0;

    // Percent of the whole machine, and the same time as a count of cores kept busy.
    float processCpu = 0.0f;
    float processCores = 0.0f;
    // Percent of one core, NaN until the engine has run a frame.
    float engineThread = std::numeric_limits<float>::quiet_NaN();

    // The user address space, and how much of it is taken. The largest free block is what the next
    // large allocation has to fit in, which is what runs a 32-bit process out of memory.
    uint64_t addressLimit = 0;
    uint64_t committed = 0;
    uint64_t reserved = 0;
    uint64_t largestFree = 0;

    uint64_t privateBytes = 0;
    uint64_t workingSet = 0;

    // Task Manager's GPU numbers for this process, when Windows has them.
    bool gpuKnown = false;
    float gpuUsage = 0.0f;
    uint64_t vramDedicated = 0;
    uint64_t vramShared = 0;

    // The worst of each since launch.
    uint64_t peakCommitted = 0;
    uint64_t peakPrivate = 0;
    uint64_t lowestLargestFree = std::numeric_limits<uint64_t>::max();
};

// Starts the sampler. Call once, from the overlay's install.
void Start();

// Safe from any thread.
Snapshot Read();

// The processor's name as Windows reports it.
const std::string& CpuName();

inline float Megabytes(uint64_t bytes) { return static_cast<float>(bytes) / (1024.0f * 1024.0f); }

}
