#pragma once

// Experimental milestone toward docs/docs/engine-internals/magma-menu-system.md's "If you want to
// build a real, separate 'Mods' page" roadmap (steps 2+3): a hand-rolled, non-compiled-in
// CGameMenu page, inserted directly into the engine's own page hashtable under an invented
// CStringID, reached by a real CSetNextPageMenuHandler wired to a new button on the Options
// screen. Deliberately NOT step 4 (real Magma visuals) yet - the page's "activate" slot only
// logs, so success is judged from bin\fcse.log (does "ModPage: ACTIVATED" appear exactly when the
// new button is clicked, and does the game not crash), not from anything appearing on screen.
namespace FCSE {

class ModPage {
public:
    // Registers the hand-rolled page into the CGameMenu owning `optionsMenuThis` and appends a
    // button to the Options screen wired to switch to it. Call once, with the same
    // optionsMenuThis/AddButton-safe `this` mods_tab.cpp's AppendModsRows already uses - see
    // mod_page.cpp for the full RE trail this relies on. Safe to call more than once (no-ops after
    // the first successful install).
    static void Install(void* optionsMenuThis);
};

} // namespace FCSE
