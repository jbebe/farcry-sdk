#pragma once

// Builds FCSE's "Mod Configuration Menu" by reusing the game's own, real, already-fully-
// initialized "Game" options page (CFCXOptionGamePage) - not a hand-rolled page inserted into
// CGameMenu's hashtable (crashed in CGameMenu_PageTable_InsertNode, abandoned - see
// docs/docs/engine-internals/magma-menu-system.md's "CGameMenu's page hashtable" section) and not
// a privately-constructed second instance either (also abandoned 2026-08-04 - crashed inside
// CGameMenu::SwitchPage's activate call, most likely because manually replicating everything the
// engine's own boot-time construction path sets up on a page object is an open-ended chase: fixing
// one missing field, +0xec, still left it crashing at the same point, meaning at least one more
// piece of state - never identified - was still missing).
//
// The actual mechanism: two different routes reach the exact same real CFCXOptionGamePage object -
// the stock "Game" button (untouched, native `CSetNextPageMenuHandler`) and FCSE's own "Mod
// Configuration Menu" button (a hand-rolled click handler that calls the same two lower-level
// native functions `CSetNextPageMenuHandler::SwitchPage` itself calls -
// `CGameMenu::SetNextPage`/`CGameMenu::SwitchPage` - directly, skipping only the redundant `GetPage`
// pre-check and the UI transition sound). Because both routes land on the identical, real,
// natively-constructed page, there is no missing-initialization risk at all.
//
// What makes the two routes behave differently is a single flag: FCSE's click handler sets it right
// before triggering the switch. CFCXOptionGamePage::RefreshOptionList (the real per-page content
// builder - reruns every time the page is displayed, unlike the Options screen's one-shot `Setup`;
// see mod_page.cpp for how this was found) is hooked once, globally; its detour calls the original
// first (always - native settings render either way), then appends FCSE's content only if the flag
// is set, clearing it immediately after. Net effect: the stock "Game" button shows native settings
// only; "Mod Configuration Menu" shows native settings plus FCSE's rows.
namespace FCSE {

class ModPage {
public:
    // Adds the "Mod Configuration Menu" button to Options (optionsMenuThis - the same
    // AddButton-safe `this` mods_tab.cpp's caller already has) and installs the RefreshOptionList
    // hook. Call once, from the same hook context mods_tab.cpp already uses. Safe to call more than
    // once (no-ops after the first successful install).
    static void Install(void* optionsMenuThis);
};

} // namespace FCSE
