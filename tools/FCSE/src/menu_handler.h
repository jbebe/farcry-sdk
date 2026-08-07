#pragma once

#include "settings_registry.h"

// A minimal, FCSE-owned stand-in for Dunia.dll's IMenuItemHandler interface - not a copy of the
// engine's real class, just an object shaped like *something* the engine's menu click-dispatch
// code can call through a vtable pointer.
//
// Confirmed live (via GhidraMCP against FarCry2_server's unstripped symbols) that the real
// IMenuItemHandler is a tiny interface: Activate(unsigned int), ActivateParent(unsigned int), plus
// a virtual destructor - nothing like the full CMenuPage class hierarchy. What's NOT confirmed is
// which vtable slot the Windows/MSVC Dunia.dll build actually calls for Activate - GCC (the Linux
// server's compiler) and MSVC lay out vtables differently, so the server's slot order doesn't
// transfer numerically. menu_handler.cpp's kActivateSlot is the single knob to turn if in-game
// testing shows a click isn't reaching OnActivate: every other slot is a safe no-op, so this object
// degrades to "does nothing when clicked" rather than crashing if the guess is wrong.
namespace FCSE {

struct ModsMenuHandler {
    void** vtable; // must stay the first member - what the engine's click-dispatch code reads

    // Which registered setting this row's clicks toggle. Points into SettingsRegistry's own
    // individually-allocated, never-moved storage, so it stays valid for the session even if more
    // plugins register afterwards. Null for a non-interactive row (a header or a plugin name).
    SettingsRegistry::Setting* setting;

    // Ordinary (non-virtual, non-static) member functions - MSVC always compiles these __thiscall
    // on x86, which is what lets menu_handler.cpp extract their raw code address (via a
    // member-function-pointer union) to populate the hand-built vtable array. Not real C++ virtual
    // dispatch - the engine's own click-dispatch code is what actually calls through `vtable`,
    // these just need to exist at the right calling convention for that to work.
    unsigned int SafeNoOp(unsigned int arg);
    unsigned int OnActivate(unsigned int arg);

    // Allocates and constructs one handler bound to `setting`. Never freed - see mod_page.cpp's
    // comment on why a small, one-time-per-session leak is the accepted tradeoff for both this and
    // the label strings it's paired with.
    static ModsMenuHandler* Create(SettingsRegistry::Setting* setting);
};

} // namespace FCSE
