#include "menu_handler.h"

#include "mod_page.h"

namespace FCSE {

namespace {
    // Generous slot count so a wrong kActivateSlot guess still can't index past the array (would
    // read garbage/crash) - every slot except kActivateSlot is a safe no-op, so worst case for a
    // wrong guess is "the row doesn't respond to clicks yet", not a crash. See menu_handler.h.
    constexpr int kVtableSlotCount = 8;
    constexpr int kActivateSlot = 1; // <-- adjust this and rebuild if empirical testing says so

    // MSVC represents a pointer to a non-virtual, non-static member function of a
    // single-inheritance, non-polymorphic class as a plain code address (same size/bit pattern as
    // a plain void*) - this union is the standard, well-precedented way to pull that raw address
    // out for storing in a hand-built vtable array that isn't real C++ virtual dispatch.
    using MemberFn = unsigned int (ModsMenuHandler::*)(unsigned int);

    void* RawFunctionPointer(MemberFn fn) {
        union {
            MemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_vtable[kVtableSlotCount];
    bool g_vtableReady = false;

    void** EnsureVtable() {
        if (!g_vtableReady) {
            void* noOp = RawFunctionPointer(&ModsMenuHandler::SafeNoOp);
            for (void*& slot : g_vtable) {
                slot = noOp;
            }
            g_vtable[kActivateSlot] = RawFunctionPointer(&ModsMenuHandler::OnActivate);
            g_vtableReady = true;
        }
        return g_vtable;
    }
}

unsigned int ModsMenuHandler::SafeNoOp(unsigned int /*arg*/) { return 0; }

unsigned int ModsMenuHandler::OnActivate(unsigned int /*arg*/) {
    // Header and plugin-name rows carry a null setting - they exist to be looked at, not clicked,
    // and must not trigger a rebuild.
    if (setting == nullptr) {
        return 0;
    }

    // The registry owns the flip, the plugin callback and the write to fcse.ini; this handler's
    // only job is to say which row was hit, then have the page redraw so the row's [ON]/[OFF]
    // reflects the value that just changed. Row labels are built from the registry's live values,
    // so rebuilding is all it takes - see ModPage::RefreshRows for what that re-entry costs.
    SettingsRegistry::ToggleCheckbox(setting);
    ModPage::RefreshRows();
    return 0;
}

ModsMenuHandler* ModsMenuHandler::Create(SettingsRegistry::Setting* setting) {
    auto* handler = new ModsMenuHandler();
    handler->vtable = EnsureVtable();
    handler->setting = setting;
    return handler;
}

} // namespace FCSE
