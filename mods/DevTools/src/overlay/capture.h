// The engine's benchmark runs, measured while the benchmark counts frames and saved as one file per
// run under bin\DevTools-benchmarks\, and every saved run to compare.
#pragma once

#include "engine/frame_stats.h"

#include <array>
#include <string>
#include <vector>

namespace DevTools::Capture {

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
    CommittedPeak,
    PrivatePeak,
    LargestFreeLow,
    VramPeak,
    AddressLimit,
    kMetricCount
};

struct MetricInfo {
    // The key in a run's file.
    const char* key;
    const char* label;
    const char* format;
    // 1 when lower is better, -1 when higher is, 0 when neither.
    int better;
};

const MetricInfo& Describe(Metric metric);

using Values = std::array<float, kMetricCount>;

constexpr Values Unmeasured() {
    Values values{};
    values.fill(FrameStats::kUnmeasured);
    return values;
}

struct Run {
    // -benchmarkid, or the date when none was given; also the file's name.
    std::string id;
    std::string date;
    float seconds = 0.0f;
    unsigned loops = 0;
    FrameStats::Display display;
    std::string plugins;
    Values values = Unmeasured();
};

// Call once a frame, on the thread that presents.
void Add(const FrameStats::Frame& frame);

// Call when the benchmark has written a loop's report. Saves the run so far.
void Report();

// Seconds this session's benchmark has measured, 0 when none has run.
float Measured();

// Every saved run, oldest first, this session's included.
std::vector<Run> Runs();

}
