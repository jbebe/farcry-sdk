#include "engine/actions.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdio>
#include <fstream>
#include <mutex>
#include <sstream>
#include <utility>
#include <vector>

namespace {
    // CActionValue, as the demo reader fills it in.
    struct Action {
        uint32_t signal;
        float values[3];
        uint32_t valueCount;
    };
    static_assert(sizeof(Action) == 0x14, "the terminal's listeners read this as their own");

    struct Timed {
        double seconds;
        Action action;
    };

    using InputPassFn = void(__fastcall*)(void* terminal, void* unused, void* context, void* inputs);
    using ExecuteFn = void(__fastcall*)(void* sink, void* unused, const Action* action, bool sameMode);

    FCSE::Relocation<InputPassFn> g_inputPass{FCSE::Uplay(0x004F1440)};
    InputPassFn g_original = nullptr;
    ExecuteFn g_originalExecute = nullptr;
    bool g_listenerHookTried = false;

    // What the terminal hands each action to, whose first virtual method takes it.
    constexpr ptrdiff_t kTerminalSink = 0x0C;

    std::mutex g_lock;
    std::vector<Action> g_pending;

    std::atomic<bool> g_recording{false};
    std::vector<Timed> g_recorded;
    ULONGLONG g_recordStart = 0;

    std::vector<Timed> g_playback;
    size_t g_playbackNext = 0;
    ULONGLONG g_playbackStart = 0;

    double SecondsSince(ULONGLONG start) { return (GetTickCount64() - start) / 1000.0; }

    void __fastcall ExecuteDetour(void* sink, void* unused, const Action* action, bool sameMode) {
        if (g_recording) {
            std::lock_guard<std::mutex> held(g_lock);
            g_recorded.push_back({SecondsSince(g_recordStart), *action});
        }
        g_originalExecute(sink, unused, action, sameMode);
    }

    void __fastcall InputPassDetour(void* terminal, void* unused, void* context, void* inputs) {
        g_original(terminal, unused, context, inputs);

        void* sink = DevTools::Read<void*>(terminal, kTerminalSink);

        // Only here is the listener known, and it is not running: its one caller has just returned.
        if (g_recording && !g_listenerHookTried) {
            g_listenerHookTried = true;
            FCSE::ApiPointer()->Hook(DevTools::Method<void*>(sink, 0), reinterpret_cast<void*>(&ExecuteDetour),
                                     reinterpret_cast<void**>(&g_originalExecute));
        }

        std::vector<Action> ready;
        {
            std::lock_guard<std::mutex> held(g_lock);
            ready.swap(g_pending);

            const double now = SecondsSince(g_playbackStart);
            while (g_playbackNext < g_playback.size() && g_playback[g_playbackNext].seconds <= now) {
                ready.push_back(g_playback[g_playbackNext++].action);
            }
        }

        for (const Action& action : ready) {
            DevTools::Method<ExecuteFn>(sink, 0)(sink, nullptr, &action, true);
        }
    }
}

namespace DevTools::Actions {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_inputPass || !api->Hook(reinterpret_cast<void*>(g_inputPass.address()),
                                   reinterpret_cast<void*>(&InputPassDetour),
                                   reinterpret_cast<void**>(&g_original))) {
        api->Log("actions: the terminal's input pass was not hooked - scripted input does nothing");
    }
}

void Send(uint32_t signal, std::span<const float> values) {
    Action action{signal, {}, static_cast<uint32_t>(std::min<size_t>(values.size(), 3))};
    std::copy_n(values.begin(), action.valueCount, action.values);

    std::lock_guard<std::mutex> held(g_lock);
    g_pending.push_back(action);
}

void StartRecording() {
    std::lock_guard<std::mutex> held(g_lock);
    g_recorded.clear();
    g_recordStart = GetTickCount64();
    g_recording = true;
}

std::string StopRecording(const std::string& path) {
    g_recording = false;

    std::vector<Timed> recorded;
    {
        std::lock_guard<std::mutex> held(g_lock);
        recorded.swap(g_recorded);
    }

    std::ofstream file(path, std::ios::trunc);
    for (const Timed& timed : recorded) {
        char line[128];
        int length = std::snprintf(line, sizeof(line), "%.3f %08X", timed.seconds, timed.action.signal);
        for (uint32_t value = 0; value < timed.action.valueCount && value < 3; ++value) {
            length += std::snprintf(line + length, sizeof(line) - length, " %g", timed.action.values[value]);
        }
        file << line << '\n';
    }
    return file ? std::string() : "the recording could not be written to " + path;
}

std::string Play(const std::string& path) {
    std::ifstream file(path);
    if (!file) {
        return "the recording could not be read from " + path;
    }

    std::vector<Timed> loaded;
    for (std::string line; std::getline(file, line);) {
        std::istringstream fields(line);
        Timed timed{};
        fields >> timed.seconds >> std::hex >> timed.action.signal >> std::dec;
        while (timed.action.valueCount < 3 && fields >> timed.action.values[timed.action.valueCount]) {
            ++timed.action.valueCount;
        }
        loaded.push_back(timed);
    }

    std::lock_guard<std::mutex> held(g_lock);
    g_playback = std::move(loaded);
    g_playbackNext = 0;
    g_playbackStart = GetTickCount64();
    return {};
}

bool Recording() { return g_recording; }

bool Playing() {
    std::lock_guard<std::mutex> held(g_lock);
    return g_playbackNext < g_playback.size();
}

}
