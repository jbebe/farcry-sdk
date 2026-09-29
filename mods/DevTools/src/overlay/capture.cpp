// Frames are kept whole for the percentiles; the once-a-second process samples are folded in as they
// land. A run spans every loop of the benchmark, and is saved again at each loop's report, since the
// engine quits after the last one.
//
// Frames arrive on the thread that presents and reports on the engine's, so both take the lock.
#include "overlay/capture.h"

#include "engine/benchmark.h"
#include "engine/process_stats.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <mutex>
#include <numeric>
#include <windows.h>
#include <psapi.h>

namespace {
    using DevTools::Capture::MetricInfo;
    using DevTools::Capture::Run;
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

    // What the process samples of one run add up to.
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

    std::mutex g_lock;
    bool g_running = false;
    unsigned g_loops = 0;
    double g_elapsedMs = 0.0;
    std::vector<float> g_frames;
    std::vector<float> g_gpu;
    Process g_process;

    // Beside fcse.ini, in the folder the process was started from.
    const std::filesystem::path& Folder() {
        static const std::filesystem::path folder = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"DevTools-benchmarks";
        }();
        return folder;
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

    std::string Now(char timeSeparator) {
        SYSTEMTIME now{};
        GetLocalTime(&now);
        char text[32];
        snprintf(text, sizeof(text), "%04u-%02u-%02u %02u%c%02u", now.wYear, now.wMonth, now.wDay,
                 now.wHour, timeSeparator, now.wMinute);
        return text;
    }

    // The run's id, or the time it started when -benchmarkid was not given, as a file name.
    const std::string& RunId() {
        static const std::string id = [] {
            std::string name = DevTools::Benchmark::Id().empty() ? Now('-')
                                                                 : DevTools::Benchmark::Id();
            for (char& c : name) {
                c = std::strchr("\\/:*?\"<>|", c) != nullptr ? '_' : c;
            }
            return name;
        }();
        return id;
    }

    void Begin() {
        g_running = true;
        // Two minutes at 300 fps, so a typical run never reallocates mid-measurement.
        g_frames.reserve(120 * 300);
        g_gpu.reserve(120 * 300);
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

    Run Summarise() {
        using namespace DevTools::Capture;

        std::sort(g_frames.begin(), g_frames.end());
        std::sort(g_gpu.begin(), g_gpu.end());

        Run run;
        run.id = RunId();
        run.date = Now(':');
        run.seconds = static_cast<float>(g_elapsedMs / 1000.0);
        run.loops = g_loops;
        run.display = DevTools::FrameStats::ReadDisplay();
        run.plugins = Plugins();

        const bool sampled = g_process.cpu.count > 0;
        Values& values = run.values;
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
        return run;
    }

    void Save(const Run& run) {
        std::error_code ignored;
        std::filesystem::create_directories(Folder(), ignored);
        const std::filesystem::path path = Folder() / (run.id + ".ini");

        std::ofstream file(path, std::ios::trunc);
        file << "; A benchmark run measured by DevTools, compared on the overlay's Diagnostics "
                "tab.\n";
        file << "date = " << run.date << "\n";
        file << "seconds = " << run.seconds << "\n";
        file << "loops = " << run.loops << "\n";
        file << "width = " << run.display.width << "\n";
        file << "height = " << run.display.height << "\n";
        file << "vsync = " << (run.display.vsync ? 1 : 0) << "\n";
        file << "plugins = " << run.plugins << "\n";
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            if (!std::isnan(run.values[metric])) {
                file << kMetrics[metric].key << " = " << run.values[metric] << "\n";
            }
        }
        file.close();

        if (!file) {
            FCSE::Logf("capture: %s could not be written", path.string().c_str());
            return;
        }

        std::string line = "capture: run '" + run.id + "', loop " + std::to_string(run.loops) +
                           ", " + std::to_string(static_cast<int>(run.seconds)) + " s";
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            if (!std::isnan(run.values[metric])) {
                char value[64];
                snprintf(value, sizeof(value), " %s=%.2f", kMetrics[metric].key,
                         run.values[metric]);
                line += value;
            }
        }
        FCSE::Logf("%s", line.c_str());
    }

    Run Load(const std::filesystem::path& path) {
        std::map<std::string, std::string> entries;
        std::ifstream file(path);
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

        const auto number = [&](const char* key) {
            const auto found = entries.find(key);
            return found == entries.end() ? kUnmeasured
                                          : std::strtof(found->second.c_str(), nullptr);
        };

        Run run;
        run.id = path.stem().string();
        run.date = entries["date"];
        run.plugins = entries["plugins"];
        run.seconds = number("seconds");
        run.loops = static_cast<unsigned>(number("loops"));
        run.display = {true, static_cast<unsigned>(number("width")),
                       static_cast<unsigned>(number("height")), number("vsync") != 0.0f};
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            run.values[metric] = number(kMetrics[metric].key);
        }
        return run;
    }

    // Read from the folder once, and kept up to date by this session's reports.
    std::vector<Run>& StoredRuns() {
        static std::vector<Run> runs = [] {
            std::vector<Run> found;
            std::error_code error;
            for (const auto& entry : std::filesystem::directory_iterator(Folder(), error)) {
                if (entry.path().extension() == L".ini") {
                    found.push_back(Load(entry.path()));
                }
            }
            std::sort(found.begin(), found.end(),
                      [](const Run& a, const Run& b) { return a.date < b.date; });
            return found;
        }();
        return runs;
    }
}

namespace DevTools::Capture {

const MetricInfo& Describe(Metric metric) { return kMetrics[metric]; }

void Add(const FrameStats::Frame& frame) {
    if (!Benchmark::Measuring()) {
        return;
    }

    std::lock_guard<std::mutex> held(g_lock);
    if (!g_running) {
        Begin();
    }
    if (!std::isnan(frame.ms)) {
        g_frames.push_back(frame.ms);
        g_elapsedMs += frame.ms;
    }
    if (!std::isnan(frame.gpuMs)) {
        g_gpu.push_back(frame.gpuMs);
    }
    Fold(ProcessStats::Read());
}

void Report() {
    std::lock_guard<std::mutex> held(g_lock);
    if (!g_running || g_frames.empty()) {
        return;
    }

    ++g_loops;
    const Run run = Summarise();
    Save(run);

    std::vector<Run>& runs = StoredRuns();
    std::erase_if(runs, [&](const Run& saved) { return saved.id == run.id; });
    runs.push_back(run);
}

float Measured() {
    std::lock_guard<std::mutex> held(g_lock);
    return static_cast<float>(g_elapsedMs / 1000.0);
}

std::vector<Run> Runs() {
    std::lock_guard<std::mutex> held(g_lock);
    return StoredRuns();
}

}
