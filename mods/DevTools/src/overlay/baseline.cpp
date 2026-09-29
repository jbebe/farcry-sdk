// Frames are kept whole for the percentiles; the once-a-second process samples are folded in as they
// land. A run spans every loop of the benchmark, and is saved again at each loop's report, since the
// engine quits after the last one.
//
// Frames arrive on the thread that presents and reports on the engine's, so both take the lock.
#include "overlay/baseline.h"

#include "engine/benchmark.h"
#include "engine/process_stats.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <mutex>
#include <numeric>
#include <windows.h>
#include <psapi.h>

namespace {
    using DevTools::Baseline::MetricInfo;
    using DevTools::Baseline::Record;
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
        {"committed", "Committed", "%.0f MB", 1},
        {"private", "Private bytes", "%.0f MB", 1},
        {"largest_free", "Largest free block", "%.0f MB", -1},
        {"vram", "VRAM", "%.0f MB", 1},
    };
    static_assert(std::size(kMetrics) == DevTools::Baseline::kMetricCount);

    struct Average {
        double sum = 0.0;
        unsigned count = 0;

        void Add(float value) {
            sum += value;
            ++count;
        }

        float Get() const { return count == 0 ? kUnmeasured : static_cast<float>(sum / count); }
    };

    // The process samples of a run: averages for load, the worst of each for memory.
    struct Samples {
        unsigned lastSerial = 0;
        Average engine;
        Average cpu;
        Average gpuUsage;
        uint64_t committedPeak = 0;
        uint64_t privatePeak = 0;
        uint64_t largestFreeLow = std::numeric_limits<uint64_t>::max();
        uint64_t vramPeak = 0;
    };

    std::mutex g_lock;
    bool g_running = false;
    unsigned g_loops = 0;
    double g_elapsedMs = 0.0;
    std::vector<float> g_frames;
    std::vector<float> g_gpu;
    Samples g_samples;

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

    std::string Now() {
        SYSTEMTIME now{};
        GetLocalTime(&now);
        char text[32];
        snprintf(text, sizeof(text), "%04u-%02u-%02u %02u:%02u", now.wYear, now.wMonth, now.wDay,
                 now.wHour, now.wMinute);
        return text;
    }

    void Fold(const DevTools::ProcessStats::Snapshot& sample) {
        if (sample.serial == g_samples.lastSerial) {
            return;
        }
        g_samples.lastSerial = sample.serial;

        if (!std::isnan(sample.engineThread)) {
            g_samples.engine.Add(sample.engineThread);
        }
        g_samples.cpu.Add(sample.processCpu);
        if (sample.gpuKnown) {
            g_samples.gpuUsage.Add(sample.gpuUsage);
            g_samples.vramPeak = std::max(g_samples.vramPeak, sample.vramDedicated);
        }
        g_samples.committedPeak = std::max(g_samples.committedPeak, sample.committed);
        g_samples.privatePeak = std::max(g_samples.privatePeak, sample.privateBytes);
        g_samples.largestFreeLow = std::min(g_samples.largestFreeLow, sample.largestFree);
    }

    DevTools::Baseline::Process Collapse(const Samples& samples) {
        DevTools::Baseline::Process process;
        process.gpuUsage = samples.gpuUsage.Get();
        process.engineThread = samples.engine.Get();
        process.processCpu = samples.cpu.Get();
        if (samples.cpu.count > 0) {
            process.committedMb = Megabytes(samples.committedPeak);
            process.privateMb = Megabytes(samples.privatePeak);
            process.largestFreeMb = Megabytes(samples.largestFreeLow);
        }
        if (samples.gpuUsage.count > 0) {
            process.vramMb = Megabytes(samples.vramPeak);
        }
        return process;
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

    void Save(const Record& record) {
        std::ofstream file(File(), std::ios::trunc);
        file << "; This rig's baseline, measured by DevTools over a run of the engine's benchmark.\n";
        file << "date = " << record.date << "\n";
        file << "seconds = " << record.seconds << "\n";
        file << "loops = " << record.loops << "\n";
        file << "width = " << record.display.width << "\n";
        file << "height = " << record.display.height << "\n";
        file << "vsync = " << (record.display.vsync ? 1 : 0) << "\n";
        file << "gpu = " << record.display.gpu << "\n";
        file << "cpu = " << record.cpu << "\n";
        file << "plugins = " << record.plugins << "\n";
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            if (!std::isnan(record.values[metric])) {
                file << kMetrics[metric].key << " = " << record.values[metric] << "\n";
            }
        }
        file.close();

        if (!file) {
            FCSE::Logf("baseline: %s could not be written", File().string().c_str());
            return;
        }

        std::string line = "baseline: loop " + std::to_string(record.loops) + ", " +
                           std::to_string(static_cast<int>(record.seconds)) + " s";
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            if (!std::isnan(record.values[metric])) {
                char value[64];
                snprintf(value, sizeof(value), " %s=%.2f", kMetrics[metric].key,
                         record.values[metric]);
                line += value;
            }
        }
        FCSE::Logf("%s", line.c_str());
    }

    Record Load() {
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

        Record record;
        if (entries.empty()) {
            return record;
        }

        const auto number = [&](const char* key) {
            const auto found = entries.find(key);
            return found == entries.end() ? kUnmeasured
                                          : std::strtof(found->second.c_str(), nullptr);
        };

        record.valid = true;
        record.date = entries["date"];
        record.seconds = number("seconds");
        record.loops = static_cast<unsigned>(number("loops"));
        record.display = {true, static_cast<unsigned>(number("width")),
                          static_cast<unsigned>(number("height")), number("vsync") != 0.0f,
                          entries["gpu"]};
        record.cpu = entries["cpu"];
        record.plugins = entries["plugins"];
        for (size_t metric = 0; metric < std::size(kMetrics); ++metric) {
            record.values[metric] = number(kMetrics[metric].key);
        }
        return record;
    }

    Record& Stored() {
        static Record record = Load();
        return record;
    }
}

namespace DevTools::Baseline {

const MetricInfo& Describe(Metric metric) { return kMetrics[metric]; }

Values Measure(std::vector<float>& frames, std::vector<float>& gpu, const Process& process) {
    std::sort(frames.begin(), frames.end());
    std::sort(gpu.begin(), gpu.end());

    Values values = Unmeasured();
    values[FrameMs] = Mean(frames.begin(), frames.end());
    values[FrameMsP99] = Slowest(frames);
    values[Fps] = 1000.0f / values[FrameMs];
    // The mean of the slowest 1% of frames, as a rate.
    const size_t worst = std::min(frames.size(), std::max<size_t>(1, frames.size() / 100));
    values[FpsLow] = 1000.0f / Mean(frames.end() - worst, frames.end());
    values[GpuMs] = Mean(gpu.begin(), gpu.end());
    values[GpuMsP99] = Slowest(gpu);
    values[GpuUsage] = process.gpuUsage;
    values[EngineThread] = process.engineThread;
    values[ProcessCpu] = process.processCpu;
    values[Committed] = process.committedMb;
    values[PrivateBytes] = process.privateMb;
    values[LargestFree] = process.largestFreeMb;
    values[Vram] = process.vramMb;
    return values;
}

void Add(const FrameStats::Frame& frame) {
    if (!Benchmark::Measuring()) {
        return;
    }

    std::lock_guard<std::mutex> held(g_lock);
    if (!g_running) {
        g_running = true;
        // Two minutes at 300 fps, so a typical run never reallocates mid-measurement.
        g_frames.reserve(120 * 300);
        g_gpu.reserve(120 * 300);
        g_samples.lastSerial = ProcessStats::Read().serial;
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

    Record record;
    record.valid = true;
    record.date = Now();
    record.seconds = static_cast<float>(g_elapsedMs / 1000.0);
    record.loops = ++g_loops;
    record.display = FrameStats::ReadDisplay();
    record.cpu = ProcessStats::CpuName();
    record.plugins = Plugins();
    record.values = Measure(g_frames, g_gpu, Collapse(g_samples));

    Save(record);
    Stored() = record;
}

float Measured() {
    std::lock_guard<std::mutex> held(g_lock);
    return static_cast<float>(g_elapsedMs / 1000.0);
}

Record Read() {
    std::lock_guard<std::mutex> held(g_lock);
    return Stored();
}

}
