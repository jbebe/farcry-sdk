// Frames are kept whole for the percentiles; the once-a-second process samples are folded in as they
// land, so a capture holds one float per frame and nothing else that grows.
#include "overlay/capture.h"

#include "engine/process_stats.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <numeric>
#include <vector>
#include <windows.h>
#include <psapi.h>

namespace {
    using DevTools::Capture::MetricInfo;
    using DevTools::Capture::State;
    using DevTools::Capture::Summary;
    using DevTools::FrameStats::kUnmeasured;
    using DevTools::ProcessStats::Megabytes;

    // In Metric order.
    constexpr MetricInfo kMetrics[] = {
        {"frame_ms", "Frame time", "%.2f ms", 1},
        {"frame_ms_p99", "Frame time, slowest 1%", "%.2f ms", 1},
        {"fps", "Frame rate", "%.1f fps", -1},
        {"fps_1pct_low", "Frame rate, 1% low", "%.1f fps", -1},
        {"gpu_ms", "GPU frame time", "%.2f ms", 1},
        {"gpu_ms_p99", "GPU frame time, slowest 1%", "%.2f ms", 1},
        {"gpu_usage", "GPU usage", "%.0f%%", 1},
        {"engine_thread", "Engine thread", "%.0f%% of a core", 1},
        {"process_cpu", "Process CPU", "%.0f%% of all cores", 1},
        {"committed_peak", "Committed, peak", "%.0f MB", 1},
        {"private_peak", "Private bytes, peak", "%.0f MB", 1},
        {"largest_free_low", "Largest free block, lowest", "%.0f MB", -1},
        {"vram_peak", "VRAM, peak", "%.0f MB", 1},
        {"address_limit", "Address space", "%.0f MB", 0},
    };
    static_assert(std::size(kMetrics) == DevTools::Capture::kMetricCount);

    struct Average {
        double sum = 0.0;
        unsigned count = 0;

        void Add(float value) {
            sum += value;
            ++count;
        }

        float Get() const { return count == 0 ? kUnmeasured : static_cast<float>(sum / count); }
    };

    // What the process samples of one capture add up to.
    struct Process {
        unsigned lastSerial = 0;
        Average engine;
        Average cpu;
        Average gpuUsage;
        uint64_t committedPeak = 0;
        uint64_t privatePeak = 0;
        uint64_t largestFreeLow = std::numeric_limits<uint64_t>::max();
        uint64_t vramPeak = 0;
        uint64_t addressLimit = 0;
    };

    State g_state = State::Idle;
    float g_duration = 0.0f;
    double g_elapsedMs = 0.0;
    bool g_overlaySeen = false;
    std::vector<float> g_frames;
    std::vector<float> g_gpu;
    Process g_process;

    Summary g_last;

    // Beside fcse.ini, in the folder the process was started from.
    const std::filesystem::path& File() {
        static const std::filesystem::path file = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"DevTools-baseline.ini";
        }();
        return file;
    }

    // The file names of the DLLs loaded from bin\plugins\, joined.
    const std::string& Plugins() {
        static const std::string plugins = [] {
            std::string joined;
            HMODULE modules[1024];
            DWORD bytes = 0;
            if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &bytes)) {
                return joined;
            }

            const size_t count = std::min<size_t>(bytes / sizeof(HMODULE), std::size(modules));
            for (size_t index = 0; index < count; ++index) {
                wchar_t path[MAX_PATH];
                const std::filesystem::path file(
                    std::wstring_view(path, GetModuleFileNameW(modules[index], path, MAX_PATH)));
                if (_wcsicmp(file.parent_path().filename().c_str(), L"plugins") == 0) {
                    joined.append(joined.empty() ? "" : ", ").append(file.filename().string());
                }
            }
            return joined;
        }();
        return plugins;
    }

    void Begin() {
        g_state = State::Running;
        g_elapsedMs = 0.0;
        g_overlaySeen = false;
        // Two minutes at 300 fps, so a normal capture never reallocates mid-run.
        g_frames.reserve(120 * 300);
        g_gpu.reserve(120 * 300);
        g_process = {};
        g_process.lastSerial = DevTools::ProcessStats::Read().serial;
    }

    void Fold(const DevTools::ProcessStats::Snapshot& sample) {
        if (sample.serial == g_process.lastSerial) {
            return;
        }
        g_process.lastSerial = sample.serial;

        if (!std::isnan(sample.engineThread)) {
            g_process.engine.Add(sample.engineThread);
        }
        g_process.cpu.Add(sample.processCpu);
        if (sample.gpuKnown) {
            g_process.gpuUsage.Add(sample.gpuUsage);
            g_process.vramPeak = std::max(g_process.vramPeak, sample.vramDedicated);
        }
        g_process.committedPeak = std::max(g_process.committedPeak, sample.committed);
        g_process.privatePeak = std::max(g_process.privatePeak, sample.privateBytes);
        g_process.largestFreeLow = std::min(g_process.largestFreeLow, sample.largestFree);
        g_process.addressLimit = sample.addressLimit;
    }

    float Mean(std::vector<float>::const_iterator first, std::vector<float>::const_iterator last) {
        return first == last ? kUnmeasured
                             : static_cast<float>(std::accumulate(first, last, 0.0) /
                                                  std::distance(first, last));
    }

    // The value only 1% of `sorted` exceed.
    float Slowest(const std::vector<float>& sorted) {
        return sorted.empty() ? kUnmeasured
                              : sorted[std::min(sorted.size() - 1, sorted.size() * 99 / 100)];
    }

    std::string Today() {
        SYSTEMTIME now{};
        GetLocalTime(&now);
        char text[32];
        snprintf(text, sizeof(text), "%04u-%02u-%02u %02u:%02u", now.wYear, now.wMonth, now.wDay,
                 now.wHour, now.wMinute);
        return text;
    }

    void Log(const Summary& summary) {
        std::string line = "capture: " + std::to_string(static_cast<int>(summary.seconds)) + " s";
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            if (!std::isnan(summary.values[metric])) {
                char value[64];
                snprintf(value, sizeof(value), " %s=%.2f", kMetrics[metric].key,
                         summary.values[metric]);
                line += value;
            }
        }
        FCSE::Logf("%s", line.c_str());
    }

    void Finish() {
        using namespace DevTools::Capture;

        std::sort(g_frames.begin(), g_frames.end());
        std::sort(g_gpu.begin(), g_gpu.end());

        Summary summary;
        summary.valid = !g_frames.empty();
        summary.date = Today();
        summary.seconds = static_cast<float>(g_elapsedMs / 1000.0);
        summary.display = DevTools::FrameStats::ReadDisplay();
        summary.overlaySeen = g_overlaySeen;
        summary.plugins = Plugins();

        const bool sampled = g_process.cpu.count > 0;
        Values& values = summary.values;
        values[FrameMs] = Mean(g_frames.begin(), g_frames.end());
        values[FrameMsP99] = Slowest(g_frames);
        values[Fps] = 1000.0f / values[FrameMs];
        // The mean of the slowest 1% of frames, as a rate.
        const size_t worst = std::min(g_frames.size(), std::max<size_t>(1, g_frames.size() / 100));
        values[FpsLow] = 1000.0f / Mean(g_frames.end() - worst, g_frames.end());
        values[GpuMs] = Mean(g_gpu.begin(), g_gpu.end());
        values[GpuMsP99] = Slowest(g_gpu);
        values[GpuUsage] = g_process.gpuUsage.Get();
        values[EngineThread] = g_process.engine.Get();
        values[ProcessCpu] = g_process.cpu.Get();
        values[CommittedPeak] = sampled ? Megabytes(g_process.committedPeak) : kUnmeasured;
        values[PrivatePeak] = sampled ? Megabytes(g_process.privatePeak) : kUnmeasured;
        values[LargestFreeLow] = sampled ? Megabytes(g_process.largestFreeLow) : kUnmeasured;
        values[AddressLimit] = sampled ? Megabytes(g_process.addressLimit) : kUnmeasured;
        values[VramPeak] =
            g_process.gpuUsage.count > 0 ? Megabytes(g_process.vramPeak) : kUnmeasured;

        // The address space the tab watches is not left holding a capture's worth of frames.
        g_state = State::Idle;
        std::vector<float>().swap(g_frames);
        std::vector<float>().swap(g_gpu);

        if (summary.valid) {
            g_last = summary;
            Log(summary);
        }
    }

    Summary Load() {
        std::map<std::string, std::string> entries;
        std::ifstream file(File());
        for (std::string line; std::getline(file, line);) {
            const size_t equals = line.find('=');
            if (line.empty() || line[0] == ';' || equals == std::string::npos) {
                continue;
            }
            const size_t keyEnd = line.find_last_not_of(" \t", equals - 1);
            const size_t valueStart = line.find_first_not_of(" \t", equals + 1);
            entries[line.substr(0, keyEnd + 1)] =
                valueStart == std::string::npos ? "" : line.substr(valueStart);
        }

        Summary summary;
        if (entries.empty()) {
            return summary;
        }

        const auto number = [&](const char* key) {
            const auto found = entries.find(key);
            return found == entries.end() ? kUnmeasured
                                          : std::strtof(found->second.c_str(), nullptr);
        };

        summary.valid = true;
        summary.date = entries["date"];
        summary.plugins = entries["plugins"];
        summary.seconds = number("seconds");
        summary.display = {true, static_cast<unsigned>(number("width")),
                           static_cast<unsigned>(number("height")), number("vsync") != 0.0f};
        summary.overlaySeen = number("overlay") != 0.0f;
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            summary.values[metric] = number(kMetrics[metric].key);
        }
        return summary;
    }

    Summary& StoredBaseline() {
        static Summary baseline = Load();
        return baseline;
    }
}

namespace DevTools::Capture {

const MetricInfo& Describe(Metric metric) { return kMetrics[metric]; }

void Start(float seconds) {
    g_duration = seconds;
    g_state = State::Armed;
}

void Stop() {
    if (g_state == State::Running) {
        Finish();
    }
    g_state = State::Idle;
}

void Add(const FrameStats::Frame& frame, bool overlayVisible) {
    if (g_state == State::Armed && !overlayVisible) {
        Begin();
    }
    if (g_state != State::Running) {
        return;
    }

    if (!std::isnan(frame.ms)) {
        g_frames.push_back(frame.ms);
        g_elapsedMs += frame.ms;
    }
    if (!std::isnan(frame.gpuMs)) {
        g_gpu.push_back(frame.gpuMs);
    }
    g_overlaySeen = g_overlaySeen || overlayVisible;
    Fold(ProcessStats::Read());

    if (g_duration > 0.0f && g_elapsedMs >= g_duration * 1000.0) {
        Finish();
    }
}

State Status() { return g_state; }

float Elapsed() { return static_cast<float>(g_elapsedMs / 1000.0); }

float Duration() { return g_duration; }

const Summary& Last() { return g_last; }

const Summary& Baseline() { return StoredBaseline(); }

void SaveBaseline() {
    if (!g_last.valid) {
        return;
    }

    std::ofstream file(File(), std::ios::trunc);
    file << "; DevTools diagnostics baseline, written by Save as baseline on the overlay's "
            "Diagnostics tab.\n";
    file << "date = " << g_last.date << "\n";
    file << "plugins = " << g_last.plugins << "\n";
    file << "seconds = " << g_last.seconds << "\n";
    file << "width = " << g_last.display.width << "\n";
    file << "height = " << g_last.display.height << "\n";
    file << "vsync = " << (g_last.display.vsync ? 1 : 0) << "\n";
    file << "overlay = " << (g_last.overlaySeen ? 1 : 0) << "\n";
    for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
        if (!std::isnan(g_last.values[metric])) {
            file << kMetrics[metric].key << " = " << g_last.values[metric] << "\n";
        }
    }
    file.close();
    if (!file) {
        FCSE::Logf("capture: %s could not be written", File().string().c_str());
        return;
    }

    StoredBaseline() = g_last;
    FCSE::Logf("capture: saved as the baseline in %s", File().string().c_str());
}

}
