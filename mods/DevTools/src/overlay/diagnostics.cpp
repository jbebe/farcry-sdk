#include "overlay/diagnostics.h"

#include "engine/benchmark.h"
#include "engine/frame_stats.h"
#include "engine/process_stats.h"
#include "overlay/baseline.h"

#include "imgui.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>

namespace {
    using DevTools::Baseline::Metric;
    using DevTools::Baseline::Record;
    using DevTools::Baseline::Values;
    using DevTools::FrameStats::History;
    using DevTools::FrameStats::kUnmeasured;
    using DevTools::ProcessStats::Megabytes;
    using DevTools::ProcessStats::Snapshot;

    // A change smaller than this, in percent, is not coloured.
    constexpr float kNoise = 5.0f;

    const ImVec4 kWorse(0.95f, 0.40f, 0.35f, 1.0f);
    const ImVec4 kBetter(0.45f, 0.85f, 0.45f, 1.0f);
    const ImVec4 kCaution(0.95f, 0.75f, 0.30f, 1.0f);

    // The slots of a history something has filled, newest `count` at most.
    std::vector<float> Filled(const History& history, int count) {
        std::vector<float> values;
        for (int step = 1; step <= count; ++step) {
            const float value =
                history.values[(history.offset - step + history.count) % history.count];
            if (value > 0.0f) {
                values.push_back(value);
            }
        }
        return values;
    }

    float Mean(const std::vector<float>& values) {
        float sum = 0.0f;
        for (const float value : values) {
            sum += value;
        }
        return values.empty() ? 0.0f : sum / values.size();
    }

    // Every measure as it stands: frames over the whole history, the process as last sampled.
    Values Live(const Snapshot& sample) {
        const History frameHistory = DevTools::FrameStats::FrameTimes();
        const History gpuHistory = DevTools::FrameStats::GpuTimes();
        std::vector<float> frames = Filled(frameHistory, frameHistory.count);
        std::vector<float> gpu = Filled(gpuHistory, gpuHistory.count);

        DevTools::Baseline::Process process;
        if (sample.serial != 0) {
            process.engineThread = sample.engineThread;
            process.processCpu = sample.processCpu;
            process.committedMb = Megabytes(sample.committed);
            process.privateMb = Megabytes(sample.privateBytes);
            process.largestFreeMb = Megabytes(sample.largestFree);
        }
        if (sample.gpuKnown) {
            process.gpuUsage = sample.gpuUsage;
            process.vramMb = Megabytes(sample.vramDedicated);
        }
        return DevTools::Baseline::Measure(frames, gpu, process);
    }

    void DrawFrames(float frameMs, float gpuMs, const Snapshot& sample) {
        ImGui::SeparatorText("Frames");
        if (frameMs <= 0.0f) {
            ImGui::TextDisabled("No frame presented yet.");
            return;
        }

        const float scale = std::max(33.3f, frameMs * 2.0f);
        const History frames = DevTools::FrameStats::FrameTimes();
        const History gpu = DevTools::FrameStats::GpuTimes();
        const ImVec2 size(-1.0f, 50.0f);
        ImGui::PlotLines("##frames", frames.values, frames.count, frames.offset, "frame ms", 0.0f,
                         scale, size);
        ImGui::PlotLines("##gpu", gpu.values, gpu.count, gpu.offset, "GPU ms", 0.0f, scale, size);

        if (!DevTools::FrameStats::GpuTimed()) {
            ImGui::TextDisabled("This driver has no timestamp queries: the GPU is not timed");
        }
        if (DevTools::FrameStats::GpuTimed() && gpuMs >= frameMs * 0.9f) {
            ImGui::TextColored(kCaution, "GPU-bound: the GPU is busy for the whole frame");
        } else if (sample.engineThread >= 90.0f) {
            ImGui::TextColored(kCaution, "CPU-bound: the engine thread has no time to spare");
        } else {
            ImGui::TextDisabled("Neither is saturated: the frame rate is capped or waiting");
        }
    }

    void DrawAddressSpace(const Snapshot& sample) {
        ImGui::SeparatorText("Address space");
        if (sample.serial == 0) {
            ImGui::TextDisabled("The first sample lands a second after launch.");
            return;
        }

        const float limitGb = Megabytes(sample.addressLimit) / 1024.0f;
        const float usedGb = Megabytes(sample.committed + sample.reserved) / 1024.0f;
        char label[64];
        snprintf(label, sizeof(label), "%.2f of %.2f GB in use", usedGb, limitGb);
        ImGui::ProgressBar(usedGb / limitGb, ImVec2(-1.0f, 0.0f), label);

        const float largest = Megabytes(sample.largestFree);
        const ImVec4 colour = largest < 128.0f   ? kWorse
                              : largest < 256.0f ? kCaution
                                                 : ImGui::GetStyleColorVec4(ImGuiCol_Text);
        ImGui::TextColored(colour, "Largest free block %.0f MB", largest);
        ImGui::SameLine();
        ImGui::TextDisabled(limitGb > 2.5f ? "(large-address-aware)" : "(not large-address-aware)");
        ImGui::TextDisabled("Worst since launch: committed %.0f MB, private %.0f MB, largest free "
                            "%.0f MB",
                            Megabytes(sample.peakCommitted), Megabytes(sample.peakPrivate),
                            Megabytes(sample.lowestLargestFree));
    }

    void Cell(const char* format, float value) {
        ImGui::TableNextColumn();
        if (std::isnan(value)) {
            ImGui::TextDisabled("-");
        } else {
            ImGui::Text(format, value);
        }
    }

    void DrawRow(Metric metric, float now, float baseline) {
        const DevTools::Baseline::MetricInfo& info = DevTools::Baseline::Describe(metric);

        ImGui::TableNextRow();
        ImGui::TableNextColumn();
        ImGui::TextUnformatted(info.label);
        Cell(info.format, now);
        Cell(info.format, baseline);

        ImGui::TableNextColumn();
        if (std::isnan(now) || std::isnan(baseline) || baseline == 0.0f) {
            ImGui::TextDisabled("-");
            return;
        }
        const float change = (now - baseline) / std::fabs(baseline) * 100.0f;
        if (std::fabs(change) < kNoise) {
            ImGui::Text("%+.1f%%", change);
        } else {
            ImGui::TextColored(change * info.better > 0.0f ? kWorse : kBetter, "%+.1f%%", change);
        }
    }

    void DrawTable(const Values& now, const Record& baseline) {
        ImGui::SeparatorText("Against this rig's baseline");
        if (!ImGui::BeginTable("measures", 4,
                               ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerH |
                                   ImGuiTableFlags_SizingStretchProp)) {
            return;
        }
        ImGui::TableSetupColumn("Measure");
        ImGui::TableSetupColumn("Now");
        ImGui::TableSetupColumn("Baseline");
        ImGui::TableSetupColumn("Change");
        ImGui::TableHeadersRow();
        for (int metric = 0; metric < DevTools::Baseline::kMetricCount; ++metric) {
            DrawRow(static_cast<Metric>(metric), now[metric],
                    baseline.valid ? baseline.values[metric] : kUnmeasured);
        }
        ImGui::EndTable();
    }

    void DrawBaseline(const Record& baseline) {
        if (DevTools::Benchmark::Measuring()) {
            ImGui::TextColored(kCaution, "The benchmark is measuring a new baseline, %.0f s so far",
                               DevTools::Baseline::Measured());
        }
        if (!baseline.valid) {
            ImGui::TextWrapped("No baseline yet. Run the game's benchmark once, through FCSE, with "
                               "only DevTools in bin\\plugins: it measures this rig and saves "
                               "bin\\DevTools-baseline.ini.");
            return;
        }

        ImGui::TextDisabled("Baseline from %s, %.0f s at %ux%u", baseline.date.c_str(),
                            baseline.seconds, baseline.display.width, baseline.display.height);
        ImGui::TextDisabled("%s, %s", baseline.display.gpu.c_str(), baseline.cpu.c_str());
        ImGui::TextDisabled("Plugins: %s", baseline.plugins.c_str());

        const DevTools::FrameStats::Display display = DevTools::FrameStats::ReadDisplay();
        const bool otherRig = display.gpu != baseline.display.gpu ||
                              DevTools::ProcessStats::CpuName() != baseline.cpu;
        if (display.known && otherRig) {
            ImGui::TextColored(kCaution, "The baseline was measured on other hardware: run the "
                                         "benchmark again on this one");
        }
        if (display.known && (display.width != baseline.display.width ||
                              display.height != baseline.display.height)) {
            ImGui::TextColored(kCaution, "The baseline ran at %ux%u, the game now at %ux%u",
                               baseline.display.width, baseline.display.height, display.width,
                               display.height);
        }
        if (baseline.display.vsync || display.vsync) {
            ImGui::TextColored(kCaution, "Vsync is on %s: frame times are capped at the refresh "
                                         "rate",
                               display.vsync ? "now" : "in the baseline");
        }
    }
}

namespace DevTools::Diagnostics {

void Draw() {
    const Snapshot sample = ProcessStats::Read();
    const Record baseline = Baseline::Read();

    // Half a second or so at the frame rates that matter.
    const float frameMs = Mean(Filled(FrameStats::FrameTimes(), 30));
    const float gpuMs = Mean(Filled(FrameStats::GpuTimes(), 30));

    DrawFrames(frameMs, gpuMs, sample);
    DrawAddressSpace(sample);
    DrawTable(Live(sample), baseline);
    DrawBaseline(baseline);
}

}
