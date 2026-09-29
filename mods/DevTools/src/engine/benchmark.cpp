// CFCXBenchmarkFrameBasedCollector, which every benchmark mode counts its frames with. When it counts
// and when it reports is in docs/docs/engine-internals/command-line-args.md.
#include "engine/benchmark.h"

#include "fcse_api.h"

#include <atomic>
#include <cctype>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <windows.h>

namespace {
    // Frames of warm-up still to run; the collector counts nothing until this reaches zero.
    constexpr ptrdiff_t kWarmUp = 0x20;

    using UpdateFn = void(__fastcall*)(void* self, void* unused, float delta);
    using SaveFn = void(__fastcall*)(void* self, void* unused, const char* file);

    // Reached only through the collector's vtable, so the address library has no entry for it.
    FCSE::Relocation<UpdateFn> g_update{FCSE::Pattern(
        "81 EC 04 02 00 00 0F 57 C0 53 56 8B F1 83 7E 20 00 8B 5E 1C 57 F3 0F 11 44 24 0C 0F 85 "
        "?? ?? ?? ?? A1 ?? ?? ?? ?? 8B 4E 08 F3 0F 10 05 ?? ?? ?? ?? F3 0F 5E 80 68 01 00 00")};
    FCSE::Relocation<SaveFn> g_save{FCSE::Uplay(0x00741A10)};

    UpdateFn g_originalUpdate = nullptr;
    SaveFn g_originalSave = nullptr;
    DevTools::Benchmark::ReportFn g_onReport = nullptr;

    std::atomic<bool> g_measuring{false};

    void __fastcall UpdateDetour(void* self, void* unused, float delta) {
        g_originalUpdate(self, unused, delta);
        g_measuring = *reinterpret_cast<int32_t*>(static_cast<uint8_t*>(self) + kWarmUp) == 0;
    }

    void __fastcall SaveDetour(void* self, void* unused, const char* file) {
        g_originalSave(self, unused, file);
        g_measuring = false;
        g_onReport();
    }

    // The value after `name` on the process's command line, unquoted.
    std::string SwitchValue(const char* name) {
        const size_t length = std::strlen(name);
        for (const char* at = GetCommandLineA(); *at != '\0'; ++at) {
            if (_strnicmp(at, name, length) != 0 || !std::isspace(static_cast<uint8_t>(at[length]))) {
                continue;
            }

            const char* value = at + length;
            while (std::isspace(static_cast<uint8_t>(*value))) {
                ++value;
            }
            const char end = *value == '"' ? '"' : ' ';
            value += end == '"' ? 1 : 0;
            const char* stop = value;
            while (*stop != '\0' && *stop != end) {
                ++stop;
            }
            return std::string(value, stop);
        }
        return {};
    }
}

namespace DevTools::Benchmark {

void Install(ReportFn onReport) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    g_onReport = onReport;

    if (!g_update || !g_save) {
        api->Log("benchmark: the frame collector was not found in this build - benchmark runs are "
                 "not measured");
        return;
    }

    // The report first: a frame counted with no report to end it is never saved, and costs nothing.
    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(reinterpret_cast<void*>(g_save.address()), reinterpret_cast<void*>(&SaveDetour),
                   reinterpret_cast<void**>(&g_originalSave)) ||
        !api->Hook(reinterpret_cast<void*>(g_update.address()),
                   reinterpret_cast<void*>(&UpdateDetour),
                   reinterpret_cast<void**>(&g_originalUpdate))) {
        return;
    }

    api->Log("benchmark: measuring the engine's benchmark runs");
}

bool Measuring() { return g_measuring; }

const std::string& Id() {
    static const std::string id = SwitchValue("-benchmarkid");
    return id;
}

}
