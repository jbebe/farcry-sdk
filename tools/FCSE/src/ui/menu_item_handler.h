#pragma once

#include "util/member_fn.h"

namespace FCSE {

// The engine's IMenuItemHandler as FCSE hands one over: a struct whose first member is a vtable
// pointer, with one real slot and the rest safe no-ops.
//
// Slot 1 is the activate slot and slot 0 is the MSVC scalar deleting destructor - established by
// instrumentation, see docs/docs/engine-internals/fcse-settings-page-abi.md.
constexpr int kEngineHandlerSlots = 8;
constexpr int kEngineHandlerActivateSlot = 1;

// `Payload` carries whatever the click needs and supplies `void OnActivate()`. It must be
// trivially copyable and hold no bases or virtuals of its own, so the member pointers below stay
// plain code addresses.
template <typename Payload>
struct MenuItemHandler {
    void** vtable; // must stay first: this is what the engine calls through
    Payload payload;

    unsigned int NoOp(unsigned int /*arg*/) { return 0; }

    unsigned int Activate(unsigned int /*arg*/) {
        payload.OnActivate();
        return 0;
    }

    // Heap-allocated and never freed: the engine keeps the pointer for as long as the row exists,
    // and FCSE's page is never destroyed. One shared vtable per payload type, built on first use.
    static MenuItemHandler* Create(const Payload& payload) {
        static void* slots[kEngineHandlerSlots];
        static bool ready = false;
        if (!ready) {
            void* noOp = RawFunctionPointer(&MenuItemHandler::NoOp);
            for (void*& slot : slots) {
                slot = noOp;
            }
            slots[kEngineHandlerActivateSlot] = RawFunctionPointer(&MenuItemHandler::Activate);
            ready = true;
        }

        auto* handler = new MenuItemHandler();
        handler->vtable = slots;
        handler->payload = payload;
        return handler;
    }
};

}
