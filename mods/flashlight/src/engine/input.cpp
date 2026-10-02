// The game's signal dispatcher: every action-map signal the player's bindings send.
#include "engine/input.h"

#include "fcse_api.h"

namespace {
    // The dispatcher's second instruction, where the signal is still the first stack argument. The
    // pattern starts past the entry, which DevTools' hook rewrites.
    FCSE::Relocation<uint8_t*> g_dispatch{FCSE::Pattern("83 EC 50 A8 01 53 55 56 57 8B F9 75 ?? 83 C8 01 A3")};

    Flashlight::Input::SignalFn g_onSignal = nullptr;

    void OnDispatch(FCSE_MidHookContext* ctx) {
        const auto* signal = *reinterpret_cast<const uint32_t* const*>(ctx->esp + 4);
        if (signal != nullptr) {
            g_onSignal(*signal);
        }
    }
}

namespace Flashlight::Input {

bool Install(SignalFn onSignal) {
    if (!g_dispatch) {
        FCSE::ApiPointer()->Log("input: the signal dispatcher was not found in this build");
        return false;
    }
    g_onSignal = onSignal;
    return FCSE::ApiPointer()->MidHook(g_dispatch.get(), &OnDispatch);
}

}
