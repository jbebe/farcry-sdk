// No save icons on map: save points lose their save disk on the map and the GPS.
//
// From Realism Plus, legacy/realism-plus/features/nav-no-save-icons.md, which blanks the
// gadgets.ObjectiveIcons.SaveDisk and SaveDiskGPS archetype names. Those names are function
// statics built on first use, so here the two CMarker::SetArchetypeByName calls that hand them to a
// save point's marker (Steam 0x1069900A, 0x10699018 / GOG 0x1068B6EA, 0x1068B6F8) are handed an
// empty name instead. A marker keeps what it was given, so a change shows on markers set up after it.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    std::atomic<bool> g_noSaveIcons{false};

    // The marker's slot-5 (map) and slot-6 (GPS) assignments, the CALLs 22 and 36 bytes in.
    FCSE::Relocation<uint8_t*> g_saveDiskCalls{FCSE::Pattern(
        "6A 05 8B CF F3 0F 11 87 68 01 00 00 C7 87 70 01 00 00 02 00 00 00 E8 ?? ?? ?? ?? "
        "68 ?? ?? ?? ?? 6A 06 8B CF E8")};
    constexpr size_t kMapIconCall = 22;
    constexpr size_t kGpsIconCall = 36;

    // The engine's std::string; the slot assignment reads only the size of its source.
    struct EngineString {
        uint32_t head;
        char buffer[16];
        uint32_t size;
        uint32_t capacity;
    };
    static_assert(sizeof(EngineString) == 0x1C, "a marker slot is 0x1C bytes");

    EngineString g_emptyName{0, {}, 0, 15};

    // The name is the second stack argument at the CALL.
    void SaveDiskHandler(FCSE_MidHookContext* ctx) {
        if (g_noSaveIcons.load(std::memory_order_relaxed)) {
            *reinterpret_cast<EngineString**>(ctx->esp + 4) = &g_emptyName;
        }
    }
}

void InstallNoSaveIcons() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_saveDiskCalls) {
        FCSE::Logf("no save icons on map: the save point's marker setup was not found in this "
                   "build - save icons stay");
        return;
    }

    const bool map = api->MidHook(g_saveDiskCalls.get() + kMapIconCall, &SaveDiskHandler);
    const bool gps = api->MidHook(g_saveDiskCalls.get() + kGpsIconCall, &SaveDiskHandler);
    if (!map || !gps) {
        FCSE::Logf("no save icons on map: a save icon call could not be hooked - that icon stays");
    }
}

void __cdecl OnNoSaveIconsChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_noSaveIcons.store(value->asCheckbox, std::memory_order_relaxed);
}
