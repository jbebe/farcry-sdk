// No reputation drop: completing A1SM03 or A2SM08 no longer lowers the player's reputation.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/reputation-kept.md.
// CFCXMissionManager::MissionCompleted sets reputation to 0x1C after A1SM03 and to 0x41 after
// A2SM08. Each name test's `je` past its reset (Steam 0x107535D9 and 0x10753601, GOG 0x10746189
// and 0x107461B1) becomes a `jmp`. Scubrah repoints both tests at "A3SM16" instead, which moves the
// A1SM03 reset onto that mission. Live.
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr size_t kTests[] = {2, 42};
    constexpr uint8_t kJe = 0x74;
    constexpr uint8_t kJmp = 0xEB;

    uint8_t* g_tests = nullptr;
}

void InstallReputationKept() {
    FCSE::Relocation<uint8_t*> tests{
        FCSE::Pattern("84 C0 74 16 6A 1C 8B CB E8 ?? ?? ?? ?? 85 FF 74 ?? 8B CF E8 ?? ?? ?? ?? "
                      "EB ?? 68 ?? ?? ?? ?? 8D 4C 24 74 E8 ?? ?? ?? ?? 84 C0 74 0B 6A 41 8B CB E8")};
    if (!tests) {
        FCSE::Logf("no reputation drop: the story missions' reputation resets were not found - "
                   "left as shipped");
        return;
    }

    g_tests = tests.get();
}

void __cdecl OnReputationKeptChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    if (g_tests == nullptr) {
        return;
    }

    const bool on = value->asCheckbox;
    const uint8_t opcode = on ? kJmp : kJe;
    bool all = true;
    for (const size_t test : kTests) {
        all &= FCSE::ApiPointer()->Patch(g_tests + test, &opcode, 1);
    }
    if (all) {
        FCSE::Logf("no reputation drop: %s", on ? "on" : "off");
    }
}
