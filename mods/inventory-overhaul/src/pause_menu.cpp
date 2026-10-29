// While CFCXPauseMenuPage::Setup runs, AddButton returns -1 for the entries the notebook took and for
// Resume, as it does without a list; see docs/docs/engine-internals/campaign.md.
#include "pause_menu.h"

#include "engine/memory.h"
#include "engine/story.h"
#include "fcse_api.h"

#include <cwchar>
#include <iterator>

namespace InventoryOverhaul::PauseMenu {

namespace {
    using SetupFn = void(__fastcall*)(void* page, void* unused);
    using AddButtonFn = int(__fastcall*)(void* page, void* unused, const wchar_t* label, char enabled, void* handler);

    FCSE::Relocation<SetupFn> g_setupSite{FCSE::Uplay(0x00853540)};
    FCSE::Relocation<AddButtonFn> g_addButtonSite{FCSE::Uplay(0x00CDBB80)};
    SetupFn g_setup = nullptr;
    AddButtonFn g_addButton = nullptr;

    // The entries' strings in the PauseMenu section; Esc resumes without Resume.
    constexpr const char* kTaken[] = {"PAUSE_RESUME", "PAUSE_REPUTATION", "PAUSE_BUDDIES", "PAUSE_JACKALFILES",
                                      "PAUSE_PREDECESSORFILES"};

    // The main page's job panel, which the Journal replaced: MissionObjective, SetupMissionObjective
    // and the Underground's line, each __thiscall(page). They show what they fill, hidden or not.
    using PanelFn = void(__fastcall*)(void* page, void* unused);

    FCSE::Relocation<PanelFn> g_panelSites[] = {
        FCSE::Relocation<PanelFn>{FCSE::Pattern(
            "81 EC E8 00 00 00 53 55 56 57 8D 44 24 11 8B E9 33 DB 50 8D 8C 24 9C 00 00 00 "
            "BE ?? ?? ?? ?? BF 0F 00 00 00 51 89 5C 24 1C 89 B4 24 9C 00 00 00")},
        FCSE::Relocation<PanelFn>{FCSE::Pattern(
            "81 EC CC 00 00 00 53 55 56 57 8D 44 24 14 8B F1 50 8D 4C 24 20 33 DB "
            "BF ?? ?? ?? ?? BD 07 00 00 00 51 89 7C 24 20 89 6C 24 38 89 5C 24 1C 89 5C")},
        FCSE::Relocation<PanelFn>{FCSE::Pattern(
            "83 EC 58 56 8B F1 83 BE B8 01 00 00 00 57 0F 84 ?? ?? ?? ?? 83 BE BC 01 00 00 00 "
            "0F 84 ?? ?? ?? ?? 83 BE C0 01 00 00 00 0F 84 ?? ?? ?? ?? 83 BE D8 01 00 00 00")},
    };
    PanelFn g_panels[std::size(g_panelSites)] = {};

    void __fastcall OnPanel(void*, void*) {}

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
    FCSE::Logf("pause menu: %s", hooked ? "Resume gone, Journal, Buddies and both tape entries left to the notebook"
                                        : "its calls were not found, so it keeps every entry");
    size_t panels = 0;
    for (size_t i = 0; i < std::size(g_panelSites); ++i) {
        panels += Hook(g_panelSites[i], &OnPanel, &g_panels[i]);
    }
    FCSE::Logf("pause menu: %zu of %zu job panel fills skipped", panels, std::size(g_panelSites));
    return hooked && panels == std::size(g_panelSites);
}

}
