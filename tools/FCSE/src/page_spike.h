#pragma once

// One-off diagnostic spike, off by default. Answers a single question before any effort goes into
// authoring an FCSE-owned .mgb page (tools/FCSE/PLAN-own-page.md):
//
//   can a privately-constructed CFCXOptionGamePage be initialised and displayed at all?
//
// Two earlier attempts at a private page (see mod_page.h's header comment) crashed inside
// CGameMenu::SwitchPage's activate call, and the chase for "one more missing field" was abandoned
// as open-ended. The 2026-08-07 research says that was never a field problem: nothing in the engine
// calls CUIPageBase::Init (Dunia.dll 0x10109410) implicitly - not CGameMenu::AddPage<T>, not
// SwitchPage - so a hand-constructed page has no bound magma::Page (this+0x14), no row ListBox
// (this+0xc) and no title Text (this+0x10). Display had nothing to display.
//
// This spike constructs a private page exactly the way the abandoned attempt did, but points its
// name at a magma page that already exists in the shipped options.mgb and then calls Init()
// explicitly before switching to it. It authors no files and modifies no game data.
//
// Pass condition, all visible in bin\fcse.log: Init() completes without an SEH exception, the
// logged +0x14/+0xc/+0x10 are all non-null, and switching to the page shows a screen rather than a
// black one. That validates the whole native half of the plan and makes the remaining work pure
// content authoring plus file delivery.
//
// Enabled by adding to bin\fcse.ini:
//
//   [FCSE]
//   Page spike = true
//
// It is deliberately a separate file from mod_page.cpp: the shipped Mod Configuration Menu is
// live-confirmed working and nothing here should be able to disturb it.
namespace FCSE {

class PageSpike {
public:
    // Returns false (having done nothing) unless the ini flag above is set. `optionsMenuThis` is the
    // same AddButton-safe `this` mod_page.cpp already receives inside the CFCXOptionPage::Setup
    // hook; it supplies the live CGameMenu* via +0x140. Call once, from that same hook context.
    static bool Install(void* optionsMenuThis);

    // True if `page` is the spike's own private instance. mod_page.cpp's RefreshOptionList detour
    // asks this so it can route content here and, importantly, not cache the spike page as the real
    // Game page - the two must not be confused.
    static bool OwnsPage(void* page);

    // Appends the spike's rows. Called from mod_page.cpp's detour *after* the native rebuild and
    // the clear-rows call, the only point at which appended rows survive: the page's own
    // RefreshOptionList clears the row list on every display, so anything added earlier (including
    // at construction) is wiped before the player ever sees it.
    static void AppendRows(void* page);
};

} // namespace FCSE
