#include "overlay/diagnostics.h"

#include "engine/benchmark.h"
#include "engine/frame_stats.h"
#include "engine/process_stats.h"
#include "overlay/capture.h"

#include "imgui.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>

namespace {
    using DevTools::Capture::Metric;
    using DevTools::Capture::Run;
    using DevTools::FrameStats::History;
    using DevTools::ProcessStats::Megabytes;
    using DevTools::ProcessStats::Snapshot;

    // A change smaller than this, in percent, is not coloured.
    constexpr float kNoise = 5.0f;

    const ImVec4 kWorse(0.95f, 0.40f, 0.35f, 1.0f);
    const ImVec4 kBetter(0.45f, 0.85f, 0.45f, 1.0f);
    const ImVec4 kCaution(0.95f, 0.75f, 0.30f, 1.0f);

    // The mean of the newest `count` values, skipping the slots nothing has filled yet.
    float Recent(const History& history, int count) {
        double sum = 0.0;
        int used = 0;
        for (int step = 1; step <= count; ++step) {
            const float value =
                history.values[(history.offset - step + history.count) % history.count];
            if (value > 0.0f) {
                sum += value;
                ++used;
            }
        }
        return used == 0 ? 0.0f : static_cast<float>(sum / used);
    }

    void DrawFrames(float frameMs, float gpuMs, const Snapshot& process) {
        ImGui::SeparatorText("Frames");
        if (frameMs <= 0.0f) {
            ImGui::TextDisabled("No frame presented yet.");
            return;
        }
        ImGui::Text("%.1f fps, %.2f ms a frame", 1000.0f / frameMs, frameMs);
        if (!DevTools::FrameStats::GpuTimed()) {
            ImGui::TextDisabled("GPU time: this driver has no timestamp queries");
        } else {
            ImGui::Text("GPU %.2f ms a frame", gpuMs);
        }
        if (process.gpuKnown) {
            ImGui::Text("GPU usage %.0f%%, VRAM %.0f MB dedicated, %.0f MB shared", process.gpuUsage,
                        Megabytes(process.vramDedicated), Megabytes(process.vramShared));
        }

        const float scale = std::max(33.3f, frameMs * 2.0f);
        const History frames = DevTools::FrameStats::FrameTimes();
        const History gpu = DevTools::FrameStats::GpuTimes();
        const ImVec2 size(-1.0f, 50.0f);
        ImGui::PlotLines("##frames", frames.values, frames.count, frames.offset, "frame ms", 0.0f,
                         scale, size);
        ImGui::PlotLines("##gpu", gpu.values, gpu.count, gpu.offset, "GPU ms", 0.0f, scale, size);

        const DevTools::FrameStats::Display display = DevTools::FrameStats::ReadDisplay();
        if (display.known) {
            ImGui::TextDisabled("%ux%u, vsync %s", display.width, display.height,
                                display.vsync ? "on" : "off");
        }
    }

    void DrawCpu(float frameMs, float gpuMs, const Snapshot& process) {
        ImGui::SeparatorText("CPU");
        if (process.serial == 0) {
            ImGui::TextDisabled("The first sample lands a second after launch.");
            return;
        }

        const float engine = process.engineThread;
        if (std::isnan(engine)) {
            ImGui::TextDisabled("Engine thread: no engine frame yet");
        } else {
            ImGui::Text("Engine thread %.0f%% of a core, %.1f ms of each frame", engine,
                        engine / 100.0f * frameMs);
        }
        ImGui::Text("Whole process %.0f%% of all cores, %.1f cores' worth", process.processCpu,
                    process.processCores);

        if (DevTools::FrameStats::GpuTimed() && gpuMs >= frameMs * 0.9f) {
            ImGui::TextColored(kCaution, "GPU-bound: the GPU is busy for the whole frame");
        } else if (engine >= 90.0f) {
            ImGui::TextColored(kCaution, "CPU-bound: the engine thread has no time to spare");
        } else {
            ImGui::TextDisabled("Neither is saturated: the frame rate is capped or waiting");
        }
    }

    void DrawMemory(const Snapshot& process) {
        ImGui::SeparatorText("Address space");
        if (process.serial == 0) {
            return;
        }

        const float limitGb = Megabytes(process.addressLimit) / 1024.0f;
        const float usedGb = Megabytes(process.committed + process.reserved) / 1024.0f;
        char label[64];
        snprintf(label, sizeof(label), "%.2f of %.2f GB in use", usedGb, limitGb);
        ImGui::ProgressBar(usedGb / limitGb, ImVec2(-1.0f, 0.0f), label);

        ImGui::TextDisabled(limitGb > 2.5f ? "Large-address-aware: the game may use 4 GB"
                                           : "Not large-address-aware: the game may use 2 GB");

        const float largest = Megabytes(process.largestFree);
        const ImVec4 colour = largest < 128.0f   ? kWorse
                              : largest < 256.0f ? kCaution
                                                 : ImGui::GetStyleColorVec4(ImGuiCol_Text);
        ImGui::TextColored(colour, "Largest free block %.0f MB", largest);
        ImGui::Text("Committed %.0f MB, reserved %.0f MB", Megabytes(process.committed),
                    Megabytes(process.reserved));
        ImGui::Text("Private %.0f MB, working set %.0f MB", Megabytes(process.privateBytes),
                    Megabytes(process.workingSet));
        ImGui::TextDisabled("Worst since launch: committed %.0f MB, private %.0f MB, largest free "
                            "%.0f MB",
                            Megabytes(process.peakCommitted), Megabytes(process.peakPrivate),
                            Megabytes(process.lowestLargestFree));
    }

    // Which saved runs are compared, as indexes into Capture::Runs(); -1 until the tab first draws.
    int g_baseline = -1;
    int g_compared = -1;

    void DrawBenchmark() {
        ImGui::SeparatorText("Benchmark");
        if (DevTools::Benchmark::Measuring()) {
            ImGui::Text("The benchmark is measuring, %.0f s so far", DevTools::Capture::Measured());
            return;
        }
        ImGui::TextWrapped("Launch with -benchmark and -benchmarkid \"<name>\". A run is measured "
                           "while the benchmark counts frames, and saved to "
                           "bin\\DevTools-benchmarks\\<name>.ini.");
    }

    void PickRun(const char* label, const std::vector<Run>& runs, int& picked) {
        ImGui::SetNextItemWidth(220.0f);
        if (ImGui::BeginCombo(label, runs[picked].id.c_str())) {
            for (int index = 0; index < static_cast<int>(runs.size()); ++index) {
                ImGui::PushID(index);
                if (ImGui::Selectable(runs[index].id.c_str(), index == picked)) {
                    picked = index;
                }
                ImGui::PopID();
            }
            ImGui::EndCombo();
        }
        const Run& run = runs[picked];
        ImGui::SameLine();
        ImGui::TextDisabled("%s, %.0f s over %u loop%s", run.date.c_str(), run.seconds, run.loops,
                            run.loops == 1 ? "" : "s");
    }

    void DrawWarnings(const Run& baseline, const Run& compared) {
        if (baseline.display.vsync || compared.display.vsync) {
            ImGui::TextColored(kCaution, "Vsync was on for a run: its frame times are capped at "
                                         "the refresh rate");
        }
        if (baseline.display.width != compared.display.width ||
            baseline.display.height != compared.display.height) {
            ImGui::TextColored(kCaution, "The runs are at different resolutions: %ux%u and %ux%u",
                               baseline.display.width, baseline.display.height,
                               compared.display.width, compared.display.height);
        }
        if (baseline.plugins != compared.plugins) {
            ImGui::TextWrapped("Baseline plugins: %s", baseline.plugins.c_str());
            ImGui::TextWrapped("Compared plugins: %s", compared.plugins.c_str());
        }
    }

    void Cell(const char* format, float value) {
        ImGui::TableNextColumn();
        if (std::isnan(value)) {
            ImGui::TextDisabled("-");
        } else {
            ImGui::Text(format, value);
        }
    }

    void DrawCompareRow(Metric metric, const Run& baseline, const Run& compared) {
        const DevTools::Capture::MetricInfo& info = DevTools::Capture::Describe(metric);
        const float before = baseline.values[metric];
        const float now = compared.values[metric];

        ImGui::TableNextRow();
        ImGui::TableNextColumn();
        ImGui::TextUnformatted(info.label);
        Cell(info.format, before);
        Cell(info.format, now);

        ImGui::TableNextColumn();
        if (std::isnan(before) || std::isnan(now) || before == 0.0f) {
            ImGui::TextDisabled("-");
            return;
        }
        const float change = (now - before) / std::fabs(before) * 100.0f;
        const float worse = change * static_cast<float>(info.better);
        if (info.better == 0 || std::fabs(change) < kNoise) {
            ImGui::Text("%+.1f%%", change);
        } else {
            ImGui::TextColored(worse > 0.0f ? kWorse : kBetter, "%+.1f%%", change);
        }
    }

    void DrawCompare() {
        const std::vector<Run> runs = DevTools::Capture::Runs();
        ImGui::SeparatorText("Runs compared");
        if (runs.empty()) {
            ImGui::TextDisabled("No run saved yet.");
            return;
        }

        // The newest run against the one before it, until either is picked.
        const int newest = static_cast<int>(runs.size()) - 1;
        if (g_compared < 0 || g_compared > newest) {
            g_compared = newest;
        }
        if (g_baseline < 0 || g_baseline > newest) {
            g_baseline = std::max(0, newest - 1);
        }
        PickRun("Baseline", runs, g_baseline);
        PickRun("Compared", runs, g_compared);

        const Run& baseline = runs[g_baseline];
        const Run& compared = runs[g_compared];
        DrawWarnings(baseline, compared);
        if (ImGui::BeginTable("compare", 4,
                              ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerH |
                                  ImGuiTableFlags_SizingStretchProp)) {
            ImGui::TableSetupColumn("Measure");
            ImGui::TableSetupColumn("Baseline");
            ImGui::TableSetupColumn("Compared");
            ImGui::TableSetupColumn("Change");
            ImGui::TableHeadersRow();
            for (int metric = 0; metric < DevTools::Capture::kMetricCount; ++metric) {
                DrawCompareRow(static_cast<Metric>(metric), baseline, compared);
            }
            ImGui::EndTable();
        }
    }
}

namespace DevTools::Diagnostics {

void Draw() {
    const Snapshot process = ProcessStats::Read();
    // Half a second or so at the frame rates that matter.
    const float frameMs = Recent(FrameStats::FrameTimes(), 30);
    const float gpuMs = Recent(FrameStats::GpuTimes(), 30);

    DrawFrames(frameMs, gpuMs, process);
    DrawCpu(frameMs, gpuMs, process);
    DrawMemory(process);
    DrawBenchmark();
    DrawCompare();
}

}
