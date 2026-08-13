#include "ui/page_internal.h"

#include "log.h"
#include "ui/fcse_page.h"
#include "util/member_fn.h"
#include "util/seh.h"

#include <cstdint>

namespace FCSE {
namespace page {

    // FCSE's three vtable overrides. MSVC will not let a free function be __thiscall, so each is a
    // real member function on a throwaway type - `this` is the engine's page, never an instance of
    // this struct. Reached only through g_pageVtable, which only FCSE's own page points at, so
    // unlike the hook these replaced there is no other instance to tell apart.
    // Each signature has to match the slot it replaces exactly, because __thiscall is
    // callee-cleanup: a thunk declaring the wrong number of stack arguments unbalances the caller's
    // stack rather than merely misbehaving. Display takes none (the stock body ends in a plain
    // RET); Update and OnSettingChanged take one each (both stock bodies end in RET 4).
    namespace {

    struct PageVtableThunk {
        void Display();
        void Update(float deltaTime);
        void OnSettingChanged(void* action);
        void ApplySettings();
        void RefreshSettings();
    };

    // Slot +0x08. The stock body is { RefreshOptionList(); this+0x200 = 0; base::Display(); }; this
    // is the same with our own content build in place of the Game tab's.
    void PageVtableThunk::Display() {
        void* page = reinterpret_cast<void*>(this);
        if (g_pageReady) {
            RebuildRows(page);
        }
        // Else: this is the display Init() triggers, while the page's widgets are still being
        // bound. Chaining straight to the base leaves an empty page for the fraction of a second
        // before the player can reach it, and the next display builds it properly.

        DWORD code = 0;
        SehWritePointer(page, kDisplayResetFieldOffset, nullptr, &code);
        if (!SehCall(&code, g_baseOptionPageDisplay, page)) {
            LogFailed("CFCXBaseOptionPage::Display", code);
        }
    }

    // Slot +0x10. The stock body is the base class's per-frame tick followed by a switch on
    // this+0x200 that drives the Game tab's difficulty/machete message-box flow - and one of that
    // switch's three branches reaches the option ids. Forwarding to the base and stopping is what
    // makes that state field inert: several functions in this class write it, but this slot is its
    // only reader, so with the switch gone it does not matter who sets it.
    void PageVtableThunk::Update(float deltaTime) {
        void* page = reinterpret_cast<void*>(this);
        DWORD code = 0;

        // Also where the "unsaved changes" flag is kept clear. Doing it here rather than only in
        // OnSettingChanged means it does not matter whether CFCXBaseOptionPage::SetDirty runs before
        // or after the change notification, or which other path might set it - by the time the
        // player can press Back, a frame has gone by and the flag is false. One byte a frame.
        SehWriteByte(page, kDirtyFlagOffset, 0, &code);

        // And where typed text is captured. Every other control announces itself through the
        // OnSettingChanged slot, but a Text row has no CUISettingBase behind it, so the engine has
        // nothing to announce - and a player who types and then backs out never triggers the rebuild
        // that would otherwise read the field. Polling is the only thing that sees those edits.
        //
        // Cheap despite the frequency: SetValue drops a value that has not changed, so a frame where
        // nothing moved costs one read per live row and touches neither the registry nor the file.
        SyncValuesFromControls();

        // Acted on here, one frame after it was asked for, so the rows a requester was walking are
        // long since out of scope.
        if (g_rebuildRequested) {
            g_rebuildRequested = false;
            RebuildRows(page);
        }

        if (!SehCall(&code, g_baseUpdate, page, deltaTime)) {
            LogFailed("CUIPageBase::Update", code);
        }
    }

    // Slot +0x4c, and the one that was crashing the game on every value change.
    //
    // CSettingsPage::OnActionSignal (0x10cdde80) offers each incoming action to every setting on the
    // page; when one consumes it - which is how a row's value actually changes - the base calls this
    // slot through the *primary* vtable, with the action. The stock body forwards to FUN_1081f6c0,
    // which looks up SETTING_DIFFICULTY's button id, gets a setting of the wrong type back, nulls
    // the pointer and dereferences it anyway.
    //
    // So this slot is both the fault and the fix: it is the engine telling us, at exactly the right
    // moment, that a value changed. The setting has already updated itself by the time we are
    // called, so reading the controls back here is all that is needed.
    void PageVtableThunk::OnSettingChanged(void* /*action*/) {
        SyncValuesFromControls();

        // The change is already in fcse.ini by the line above, so the page has nothing pending and
        // must not claim otherwise when the player backs out. Update() clears this too, belt and
        // braces - see the comment there.
        DWORD code = 0;
        SehWriteByte(reinterpret_cast<void*>(this), kDirtyFlagOffset, 0, &code);
    }

    // Slots +0x50 and +0x54 - the engine's "apply my settings to the game options" and "reload my
    // settings from the game options". Both are meaningless for a page whose settings are FCSE's,
    // and both walk the same option ids: see the vtable comment at the top of this file.
    //
    // Apply is not merely dropped, it is repurposed. It is the engine telling us a value changed,
    // which is exactly when fcse.ini should be written - so this is also what makes a toggle persist
    // on the click rather than on the next display.
    void PageVtableThunk::ApplySettings() {
        SyncValuesFromControls();
    }

    void PageVtableThunk::RefreshSettings() {
        // Nothing to reload from: FCSE's values live in the registry, and the controls were seeded
        // from it when the rows were built.
    }

    }

    // Point the page at FCSE's own copy of its class vtable, with the three Game-tab-specific slots
    // replaced. Must run before Init(), because Init triggers a display and the stock Display is
    // what would otherwise build - and bind ids to - the Game tab's own rows.
    bool InstallPageVtable(void* page, uintptr_t vtableAddress) {
        DWORD code = 0;
        for (size_t slot = 0; slot < kPageVtableSlots; ++slot) {
            void* value = nullptr;
            if (!SehReadPointer(reinterpret_cast<void*>(vtableAddress),
                                 static_cast<ptrdiff_t>(slot * sizeof(void*)), &value, &code)) {
                LogFailed("reading CFCXOptionGamePage's vtable", code);
                return false;
            }
            g_pageVtable[slot] = value;
        }

        g_pageVtable[kDisplaySlot] = RawFunctionPointer(&PageVtableThunk::Display);
        g_pageVtable[kUpdateSlot] = RawFunctionPointer(&PageVtableThunk::Update);
        g_pageVtable[kSettingChangedSlot] = RawFunctionPointer(&PageVtableThunk::OnSettingChanged);
        g_pageVtable[kApplySlot] = RawFunctionPointer(&PageVtableThunk::ApplySettings);
        g_pageVtable[kRefreshSlot] = RawFunctionPointer(&PageVtableThunk::RefreshSettings);

        if (!SehWritePointer(page, 0, g_pageVtable, &code)) {
            LogFailed("writing the page's vtable pointer", code);
            return false;
        }
        Log::Loader("FcsePage: installed a private " + std::to_string(kPageVtableSlots) +
                    "-slot vtable - Display, Update, OnSettingChanged, apply and refresh are "
                    "FCSE's; the stock Game tab keeps the engine's table");
        return true;
    }

}
}
