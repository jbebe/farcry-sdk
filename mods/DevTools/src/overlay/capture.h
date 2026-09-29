// A timed run of frame, CPU and memory measurements, summarised, and the baseline it is compared
// against. Everything here is on the thread that presents.
#pragma once

#include "engine/frame_stats.h"

#include <array>
#include <string>

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
    // The key in the baseline file.
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

struct Summary {
    bool valid = false;
    std::string date;
    float seconds = 0.0f;
    FrameStats::Display display;
    // The overlay was open for part of the run, so its own drawing is in the frame times.
    bool overlaySeen = false;
    std::string plugins;
    Values values = Unmeasured();
};

enum class State { Idle, Armed, Running };

// Arms a capture, which starts once the overlay closes. Zero seconds runs until Stop.
void Start(float seconds);
void Stop();

// Call once a frame, whether or not the overlay is up.
void Add(const FrameStats::Frame& frame, bool overlayVisible);

State Status();
float Elapsed();
float Duration();

// The last finished capture this session.
const Summary& Last();

// Read from bin\DevTools-baseline.ini the first time it is asked for.
const Summary& Baseline();

// Makes the last capture the baseline and writes it to the file.
void SaveBaseline();

}
