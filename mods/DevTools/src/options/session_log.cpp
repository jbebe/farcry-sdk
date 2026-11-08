// Session log: every frame presented, written to a CSV in bin\, so two play sessions - one with a mod,
// one without - can be compared afterwards. Off by default; it changes nothing in play, but it writes
// a few megabytes every few minutes.
//
// Frames are gathered on the thread that presents and written once a second from a thread of the
// log's own, so the frames being measured pay for a push under a lock and nothing more.
#include "engine/bin_folder.h"
#include "engine/entity.h"
#include "engine/frame_stats.h"
#include "engine/pawn_tick.h"
#include "engine/player.h"
#include "engine/process_stats.h"
#include "engine/renderer.h"
#include "fcse_api.h"
#include "overlay/baseline.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <mutex>
#include <thread>
#include <vector>
#include <windows.h>

namespace {
    using Clock = std::chrono::steady_clock;
    using Seconds = std::chrono::duration<double>;

    // A frame is in play while the input pass ran this recently; it does not run in a menu or a load.
    constexpr Seconds kInPlay{0.25};

    // What the input pass saw last.
    struct Place {
        Clock::time_point at;
        bool inVehicle = false;
        float position[3] = {};
    };

    struct Row {
        Clock::time_point at;
        float ms;
        float gpuMs;
        Place place;
    };

    std::atomic<bool> g_enabled{false};

    std::mutex g_lock;
    std::vector<Row> g_rows;
    Place g_place;
    DevTools::FrameStats::Display g_display;

    // The writer thread's own.
    FILE* g_file = nullptr;
    Clock::time_point g_start;
    Clock::time_point g_end;
    size_t g_frames = 0;
    unsigned g_serial = 0;

    void OnTick(const DevTools::PawnTick::Frame& frame) {
        if (!g_enabled) {
            return;
        }

        Place place;
        place.at = Clock::now();
        place.inVehicle = DevTools::Player::Vehicle(reinterpret_cast<void*>(frame.pawn)) != nullptr;
        void* ref = DevTools::Entity::AcquireFocusRef(DevTools::Player::CameraManager(frame.local));
        if (void* entity = DevTools::Entity::Of(ref)) {
            const float* matrix = DevTools::Entity::Matrix(entity);
            std::copy(matrix + 12, matrix + 15, place.position);
        }
        DevTools::Entity::ReleaseRef(ref);

        std::lock_guard<std::mutex> held(g_lock);
        g_place = place;
    }

    void OnFrame(const DevTools::FrameStats::Frame& frame) {
        if (!g_enabled || std::isnan(frame.ms)) {
            return;
        }

        const Clock::time_point now = Clock::now();
        std::lock_guard<std::mutex> held(g_lock);
        if (!g_display.known) {
            g_display = DevTools::FrameStats::ReadDisplay();
        }
        g_rows.push_back({now, frame.ms, frame.gpuMs, g_place});
    }

    // An empty cell for a value nothing measured.
    void Cell(const char* format, float value) {
        std::fputc(',', g_file);
        if (!std::isnan(value)) {
            std::fprintf(g_file, format, value);
        }
    }

    void Open(Clock::time_point start) {
        SYSTEMTIME now{};
        GetLocalTime(&now);
        char name[64];
        std::snprintf(name, sizeof(name), "DevTools-session-%04u-%02u-%02u-%02u%02u%02u.csv",
                      now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond);
        const std::filesystem::path path =
            DevTools::BinFolder::File(std::filesystem::path(name).c_str());
        if (_wfopen_s(&g_file, path.c_str(), L"w") != 0) {
            FCSE::Logf("session log: bin\\%s could not be opened", name);
            return;
        }

        DevTools::FrameStats::Display display;
        {
            std::lock_guard<std::mutex> held(g_lock);
            display = g_display;
        }
        std::fprintf(g_file, "# DevTools session log, %04u-%02u-%02u %02u:%02u\n", now.wYear,
                     now.wMonth, now.wDay, now.wHour, now.wMinute);
        std::fprintf(g_file, "# %ux%u, vsync %d, %s, %s\n", display.width, display.height,
                     display.vsync ? 1 : 0, display.gpu.c_str(),
                     DevTools::ProcessStats::CpuName().c_str());
        std::fprintf(g_file, "# plugins: %s\n", DevTools::BinFolder::Plugins().c_str());
        std::fputs("t,frame_ms,gpu_ms,in_play,in_vehicle,x,y,z,engine_thread,process_cpu,gpu_usage,"
                   "committed_mb,private_mb,largest_free_mb,vram_mb\n",
                   g_file);
        g_start = start;
        g_frames = 0;
        g_serial = DevTools::ProcessStats::Read().serial;
        FCSE::Logf("session log: writing bin\\%s", name);
    }

    void Close() {
        std::fclose(g_file);
        g_file = nullptr;
        FCSE::Logf("session log: closed, %zu frames over %.0f s", g_frames,
                   Seconds(g_end - g_start).count());
    }

    // Each frame is a row; the process, sampled once a second, goes on the last row of the batch it
    // landed in and leaves those cells empty on the rest.
    void WriteRows(const std::vector<Row>& rows) {
        const DevTools::ProcessStats::Snapshot sample = DevTools::ProcessStats::Read();
        const bool sampled = sample.serial != g_serial;
        g_serial = sample.serial;

        for (size_t index = 0; index < rows.size(); ++index) {
            const Row& row = rows[index];
            std::fprintf(g_file, "%.4f,%.3f", Seconds(row.at - g_start).count(), row.ms);
            Cell("%.3f", row.gpuMs);
            if (row.at - row.place.at < kInPlay) {
                std::fprintf(g_file, ",1,%d,%.2f,%.2f,%.2f", row.place.inVehicle ? 1 : 0,
                             row.place.position[0], row.place.position[1], row.place.position[2]);
            } else {
                std::fputs(",0,,,,", g_file);
            }

            if (sampled && index + 1 == rows.size()) {
                const DevTools::Baseline::Process process = DevTools::Baseline::Sampled(sample);
                Cell("%.1f", process.engineThread);
                Cell("%.1f", process.processCpu);
                Cell("%.1f", process.gpuUsage);
                Cell("%.0f", process.committedMb);
                Cell("%.0f", process.privateMb);
                Cell("%.0f", process.largestFreeMb);
                Cell("%.0f", process.vramMb);
                std::fputc('\n', g_file);
            } else {
                std::fputs(",,,,,,,\n", g_file);
            }
        }
        std::fflush(g_file);
        g_frames += rows.size();
        g_end = rows.back().at;
    }

    void Run() {
        // Swapped with the frames' buffer, so each keeps its capacity and a push never grows one.
        std::vector<Row> rows;
        for (;;) {
            Sleep(1000);
            {
                std::lock_guard<std::mutex> held(g_lock);
                rows.swap(g_rows);
            }

            if (!rows.empty()) {
                if (g_file == nullptr) {
                    Open(rows.front().at);
                }
                if (g_file != nullptr) {
                    WriteRows(rows);
                }
                rows.clear();
            }
            if (g_file != nullptr && !g_enabled) {
                Close();
            }
        }
    }
}

int GetSessionLog() { return g_enabled ? 1 : 0; }

void SetSessionLog(int value) {
    if ((value != 0) == g_enabled) {
        return;
    }
    g_enabled = value != 0;

    static std::once_flag started;
    if (g_enabled) {
        std::call_once(started, [] { std::thread(&Run).detach(); });
    }
    FCSE::ApiPointer()->Log(g_enabled ? "session log: on - every frame is written to a CSV in bin"
                                      : "session log: off");
}

void InstallSessionLog() {
    DevTools::PawnTick::Subscribe(&OnTick);
    DevTools::Renderer::Subscribe(&OnFrame);
}
