// While CFCXPauseMenuPage::Setup runs, AddButton returns -1 for the entries the notebook took, as it
// does without a list; see docs/docs/engine-internals/campaign.md.
#include "pause_menu.h"

#include "engine/memory.h"
#include "engine/story.h"
#include "fcse_api.h"

#include <cwchar>

namespace InventoryOverhaul::PauseMenu {

namespace {
    using SetupFn = void(__fastcall*)(void* page, void* unused);
    using AddButtonFn = int(__fastcall*)(void* page, void* unused, const wchar_t* label, char enabled, void* handler);

    FCSE::Relocation<SetupFn> g_setupSite{FCSE::Uplay(0x00853540)};
    FCSE::Relocation<AddButtonFn> g_addButtonSite{FCSE::Uplay(0x00CDBB80)};
    SetupFn g_setup = nullptr;
    AddButtonFn g_addButton = nullptr;

    // The entries' strings in the PauseMenu section.
    constexpr const char* kTaken[] = {"PAUSE_REPUTATION", "PAUSE_BUDDIES", "PAUSE_JACKALFILES", "PAUSE_PREDECESSORFILES"};

    bool g_settingUp = false;

    void __fastcall OnSetup(void* page, void* unused) {
        g_settingUp = true;
        g_setup(page, unused);
        g_settingUp = false;
    }

    int __fastcall OnAddButton(void* page, void* unused, const wchar_t* label, char enabled, void* handler) {
        if (g_settingUp && label != nullptr) {
            for (const char* key : kTaken) {
                if (std::wcscmp(label, Story::Localize("PauseMenu", key).c_str()) == 0) {
                    return -1;
                }
            }
        }
        return g_addButton(page, unused, label, enabled, handler);
    }
}

bool Install() {
    const bool hooked = Hook(g_setupSite, &OnSetup, &g_setup) && Hook(g_addButtonSite, &OnAddButton, &g_addButton);
    FCSE::Logf("pause menu: %s", hooked ? "Journal, Buddies and both tape entries left to the notebook"
                                        : "its calls were not found, so it keeps every entry");
    return hooked;
}

}
