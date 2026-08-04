#include "menu_handler.h"

#include "log.h"

#include <string>

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
    if (field == nullptr || field->value == nullptr) {
        return 0;
    }

    *field->value = !*field->value;

    Log::Loader(std::string("Mods: '") + (field->label != nullptr ? field->label : "?") +
                "' toggled to " + (*field->value ? "ON" : "OFF"));

    if (field->onChanged != nullptr) {
        field->onChanged(field->userdata);
    }
    return 0;
}

ModsMenuHandler* ModsMenuHandler::Create(FCSE_ConfigBool* field) {
    auto* handler = new ModsMenuHandler();
    handler->vtable = EnsureVtable();
    handler->field = field;
    return handler;
}

} // namespace FCSE
