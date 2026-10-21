// Intuition: how long a soldier keeps knowing where his target really is after (re)acquiring it,
// seen or not.
#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // CPawnAgent::ManageIntuition's comparison against the window, whose operand is kWindowOperand
    // bytes in.
    FCSE::Relocation<uint8_t*> g_windowRead{
        FCSE::Pattern("A1 ?? ?? ?? ?? F2 0F 10 40 30 F3 0F 10 0D ?? ?? ?? ?? 66 0F 5A C0 F3 0F 5C 86 "
                      "?? ?? ?? ?? 0F 2F C8")};
    constexpr ptrdiff_t kWindowOperand = 14;

    // Hardcore: seconds of the window. The game's is 6.
    float g_intuitionSeconds = 1.0f;
}

void InstallIntuition() {
    if (!RedirectOperand(g_windowRead, kWindowOperand, &g_intuitionSeconds)) {
        FCSE::Logf("intuition: the soldiers' intuition window was not found in this build");
    }
}
