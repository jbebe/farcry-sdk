// Limited saving: Save Game leaves the pause menu and F5/F9 stop working; save points still save.
//
// Realism Plus blanks CSaveGamePage and PAUSE_SAVEGAME,
// legacy/realism-plus/features/saving-no-pause-save.md, and unbinds the keys in data. This is
// instead FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/limitedsaving.ixx, which
// works live. The sites, Steam / GOG:
//
// - The pause page's PAUSE_SAVEGAME entry block (0x1085418D / 0x1084755D) jumps past itself and
//   leaves the entry's index at -1, which the page already treats as no entry.
// - CFCXPauseMenuPage::Open (0x10857150 / 0x1084A520) rebuilds the kept page's entries when the
//   setting has changed since they were built.
// - Quicksave's allowed test (0x1071B58F / 0x1070DECF) is answered no, and quickload's manager
//   fetch (0x1071B648 / 0x1070DF88) null.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>
#include <cstring>

namespace {
    std::atomic<bool> g_limited{false};

    FCSE::Relocation<uint8_t*> g_saveEntry{FCSE::Pattern(
        "53 6A 7C E8 ?? ?? ?? ?? 8B F8 83 C4 08 3B FB 74 23 39 1D ?? ?? ?? ?? 75 07 33 C9 E8 ?? ?? "
        "?? ?? 6A 01 53 68 ?? ?? ?? ?? 56 8B CF E8 ?? ?? ?? ?? 8B F8 EB 02 33 FF 68 ?? ?? ?? ?? 8D "
        "4C 24 20 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8D 4C 24 3C E8 ?? ?? ?? ?? 57 6A 01 51 8B 0D ?? ?? "
        "?? ?? 8B C4 89 08 8B 0D ?? ?? ?? ?? 8D 54 24 28 52 8D 44 24 48 50 E8 ?? ?? ?? ?? 50 8B CE "
        "E8 ?? ?? ?? ?? 8D 4C 24 38 89 86 EC 01 00 00")};

    // mov dword [esi+0x1EC], -1 / jmp +0x86, onto the next entry's block.
    constexpr uint8_t kSkipSaveEntry[] = {0xC7, 0x86, 0xEC, 0x01, 0x00, 0x00, 0xFF, 0xFF,
                                          0xFF, 0xFF, 0xE9, 0x86, 0x00, 0x00, 0x00};
    uint8_t g_saveEntryShipped[sizeof(kSkipSaveEntry)] = {};

    FCSE::Relocation<uint8_t*> g_pauseOpen{FCSE::Pattern(
        "83 EC 5C 53 55 56 57 8D 44 24 13 8B F1 50 8D 4C 24 20 33 DB BD ?? ?? ?? ?? BF 0F 00 00 00")};

    // CFCXPauseMenuPage: the save entry's index, -1 when skipped, and whether its document is up.
    constexpr uintptr_t kSaveEntryIndex = 0x1EC;
    constexpr uintptr_t kPageShown = 0x164;
    constexpr size_t kBuildEntriesSlot = 15;
    constexpr size_t kClearEntriesSlot = 16;

    using PageMethod = void(__thiscall*)(void* page);

    // The test al,al on the save manager's vtable +0x50 answer, 44 bytes in.
    FCSE::Relocation<uint8_t*> g_quickSave{FCSE::Pattern(
        "3B 05 ?? ?? ?? ?? 0F 85 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 6A 01 E8 ?? ?? ?? ?? 8B F0 85 F6 0F "
        "84 ?? ?? ?? ?? 8B 06 8B 50 50 8B CE FF D2 84 C0")};
    constexpr size_t kQuickSaveAllowed = 44;

    // The test eax,eax on the fetched manager, 21 bytes in.
    FCSE::Relocation<uint8_t*> g_quickLoad{FCSE::Pattern(
        "3B 05 ?? ?? ?? ?? 75 32 8B 0D ?? ?? ?? ?? 6A 01 E8 ?? ?? ?? ?? 85 C0 74 15 83 B8 44 01 00 "
        "00 00")};
    constexpr size_t kQuickLoadManager = 21;

    // The page is built once and kept, so its entries are rebuilt ahead of the stock open. A clear
    // declines while the document is down, and a build without one doubles the menu.
    void PauseOpenHandler(FCSE_MidHookContext* ctx) {
        auto* page = reinterpret_cast<uint8_t*>(ctx->ecx);
        if (page == nullptr || page[kPageShown] == 0) {
            return;
        }

        const bool entryGone = *reinterpret_cast<int32_t*>(page + kSaveEntryIndex) == -1;
        if (entryGone == g_limited.load(std::memory_order_relaxed)) {
            return;
        }

        void** vtable = *reinterpret_cast<void***>(page);
        reinterpret_cast<PageMethod>(vtable[kClearEntriesSlot])(page);
        reinterpret_cast<PageMethod>(vtable[kBuildEntriesSlot])(page);
    }

    void QuickSaveHandler(FCSE_MidHookContext* ctx) {
        if (g_limited.load(std::memory_order_relaxed)) {
            ctx->eax &= ~uintptr_t{0xFF};
        }
    }

    // The manager is never dereferenced on the way out.
    void QuickLoadHandler(FCSE_MidHookContext* ctx) {
        if (g_limited.load(std::memory_order_relaxed)) {
            ctx->eax = 0;
        }
    }
}

void InstallLimitedSaving() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_saveEntry) {
        FCSE::Logf("limited saving: the pause menu's save entry was not found in this build - it "
                   "stays");
    } else {
        std::memcpy(g_saveEntryShipped, g_saveEntry.get(), sizeof(g_saveEntryShipped));
        if (!g_pauseOpen || !api->MidHook(g_pauseOpen.get(), &PauseOpenHandler)) {
            FCSE::Logf("limited saving: the pause menu's open could not be hooked - a change shows "
                       "once the pause menu is next built");
        }
    }

    if (!g_quickSave || !api->MidHook(g_quickSave.get() + kQuickSaveAllowed, &QuickSaveHandler)) {
        FCSE::Logf("limited saving: quicksave could not be hooked - F5 still saves");
    }

    if (!g_quickLoad || !api->MidHook(g_quickLoad.get() + kQuickLoadManager, &QuickLoadHandler)) {
        FCSE::Logf("limited saving: quickload could not be hooked - F9 still loads");
    }
}

void __cdecl OnLimitedSavingChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const bool on = value->asCheckbox;
    if (on == g_limited.load(std::memory_order_relaxed)) {
        return;
    }

    g_limited.store(on, std::memory_order_relaxed);
    if (g_saveEntry) {
        FCSE::ApiPointer()->Patch(g_saveEntry.get(), on ? kSkipSaveEntry : g_saveEntryShipped,
                                  sizeof(kSkipSaveEntry));
    }
    FCSE::Logf(on ? "limited saving: on - save points only"
                  : "limited saving: off - the pause menu and F5/F9 save as shipped");
}
