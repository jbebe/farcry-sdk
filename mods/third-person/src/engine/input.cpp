#include "engine/input.h"

#include "fcse_api.h"

namespace {
    // The dispatcher's entry. Its first two instructions are wildcards because other plugins hook
    // them.
    FCSE::Relocation<uint8_t*> g_dispatch{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? ?? ?? ?? ?? 53 55 56 57 8B F9 75 ?? 83 C8 01 A3 ?? ?? ?? ?? C7 05 ?? ?? ?? "
        "?? ?? ?? ?? ?? A8 02 75 ?? 83 C8 02 A3 ?? ?? ?? ?? C7 05 ?? ?? ?? ?? 78 D9 03 A7")};

    ThirdPerson::Input::SignalFn g_onSignal = nullptr;

    void OnDispatch(FCSE_MidHookContext* ctx) {
        const auto* signal = *reinterpret_cast<const uint32_t* const*>(ctx->esp + 4);
        if (signal != nullptr) {
            g_onSignal(*signal);
        }
    }
}

bool ThirdPerson::Input::Install(SignalFn onSignal) {
    if (!g_dispatch) {
        FCSE::ApiPointer()->Log("input: the signal dispatcher was not found in this build");
        return false;
    }
    g_onSignal = onSignal;
    if (!FCSE::ApiPointer()->MidHook(g_dispatch.get(), &OnDispatch)) {
        FCSE::ApiPointer()->Log("input: the signal dispatcher could not be hooked");
        return false;
    }
    return true;
}
