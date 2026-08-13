#pragma once

// FCSE's own settings page: a privately constructed CFCXOptionGamePage bound to the FCSE_PAGE
// layout that magma_package.cpp loads out of fcse.mgb.
//
// This is the mechanism the 2026-08-07 spike proved and the 2026-08-08 rollback corrected. The
// spike constructed a private page, called CUIPageBase::Init on it, and reached it through
// CGameMenu::SwitchPage - all of which worked. What did not work was pointing it at a *shipped*
// layout: a private page bound to a borrowed name shares that layout's magma::Page with the stock
// class that also binds it, so after visiting the mod page the real Network tab *was* the mod page.
// Owning the layout is the whole fix, and it is why fcse.mgb had to exist first.
//
// This is now the only mod configuration surface. The earlier mechanism - appending FCSE's rows
// onto the stock Game tab and telling the two visits apart with a flag - has been deleted along
// with mod_page.{h,cpp} and menu_handler.{h,cpp}. It only ever existed because FCSE had no page of
// its own to put content on, and it came with a permanent cost: the stock Game tab and FCSE's menu
// were the same screen, so anything FCSE did there had to be undone for the next visitor.
//
// FCSE now leaves the stock page completely alone, and not by checking: the page is given a private
// copy of CFCXOptionGamePage's vtable with the three Game-tab-specific slots replaced (Display, and
// the apply/refresh pair that dereferences a null when a row it did not build is activated). The
// stock Game tab still points at the engine's own table, so it cannot be affected. Nothing in the
// process is patched - the global hook on RefreshOptionList this replaced is gone. The full trail is
// in the vtable comment at the top of fcse_page.cpp.
namespace FCSE {

class FcsePage {
public:
    // Constructs the page, gives it FCSE's vtable, binds it to FCSE_PAGE, and adds the Options row
    // that opens it. Requires MagmaPackage::Load() to have succeeded - without the package the name
    // does not resolve and a displayed page would show nothing, so this declines rather than
    // offering a dead row.
    //
    // Call once, from the Options-screen hook in mods_tab.cpp. Returns false (logged) whenever it
    // declines; never fatal.
    static bool Install(void* optionsMenuThis);

    // Appends FCSE's rows. Internal to ui/ - it must be called from inside the per-display rebuild,
    // since the row list is cleared every time the page is shown, so anything added at construction
    // time is wiped before the player ever sees it.
    static void AppendRows(void* page);
};

} // namespace FCSE
