// This rig's reference numbers: measured over a run of the engine's benchmark, saved to
// bin\DevTools-baseline.ini each time the benchmark reports, and read by the Diagnostics tab.
#pragma once

#include "engine/frame_stats.h"

#include <array>
#include <string>
#include <vector>

namespace DevTools::Baseline {

enum Metric {
    FrameMs,
    FrameMsP99,
    Fps,
    FpsLow,
    GpuMs,
    GpuMsP99,
    GpuUsage,
    EngineThread,
    ProcessCpu,
    Committed,
    PrivateBytes,
    LargestFree,
    Vram,
    kMetricCount
};

struct MetricInfo {
    // The key in the baseline file.
    const char* key;
    const char* label;
    const char* format;
    // 1 when lower is better, -1 when higher is.
    int better;
};

const MetricInfo& Describe(Metric metric);

using Values = std::array<float, kMetricCount>;

constexpr Values Unmeasured() {
    Values values{};
    values.fill(FrameStats::kUnmeasured);
    return values;
}

// What the process samples over a stretch of frames come to, NaN where nothing was sampled.
struct Process {
    float gpuUsage = FrameStats::kUnmeasured;
    float engineThread = FrameStats::kUnmeasured;
    float processCpu = FrameStats::kUnmeasured;
    float committedMb = FrameStats::kUnmeasured;
    float privateMb = FrameStats::kUnmeasured;
    float largestFreeMb = FrameStats::kUnmeasured;
    float vramMb = FrameStats::kUnmeasured;
};

// Every measure of a stretch of frames. Sorts `frames` and `gpu`.
Values Measure(std::vector<float>& frames, std::vector<float>& gpu, const Process& process);

struct Record {
    bool valid = false;
    std::string date;
    float seconds = 0.0f;
    unsigned loops = 0;
    // The resolution, vsync and GPU it was measured at.
    FrameStats::Display display;
    std::string cpu;
    std::string plugins;
    Values values = Unmeasured();
};

// Call once a frame, on the thread that presents.
void Add(const FrameStats::Frame& frame);

// Call when the benchmark has written a loop's report. Saves the run so far as the baseline.
void Report();

// Seconds this session's benchmark has measured, 0 when none has run.
float Measured();

// The saved baseline, read from the file the first time it is asked for.
Record Read();

}
