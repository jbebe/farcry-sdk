// No blinking pickups: weapons, ammo, health, diamond cases and other usables stop pulsing, and map
// and compass markers stop blinking, the save-point disk among them.
//
// Realism Plus blanks the Mesh_Highlight technique name,
// legacy/realism-plus/features/graphics-no-flashing-items.md. This is instead FC2JackalFix (MIT,
// (c) 2026 Joshhhuaaa, TGP482) source/gameplay/blinkingitems.ixx, after FoxAhead's Far Cry 2 Multi
// Fixer, which works live. The sites, Steam / GOG:
//
// - The highlight gate's TEST [item+0x9C],4 in the plain (0x103C743A / 0x103B92CA) and instanced
//   (0x103C8694 / 0x103BA524) passes gets a 0 immediate, so the highlight is always skipped.
// - CMarker::SetArchetypeByName (0x1004FB20 / 0x1004F9C0) refuses the three blink slots.
// - The two archetype-reference visitors (0x1004E820 / 0x10674080, 0x101413D0 / 0x1004E770) return
//   at once for archBlink, archBlinkCompass and archBlinkCompassVehicle.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>
#include <cstring>
#include <initializer_list>

namespace {
    std::atomic<bool> g_noBlinking{false};

    // The immediate of TEST [item+0x9C],4, 24 bytes into each match.
    constexpr size_t kGateImmediate = 24;
    constexpr uint8_t kGateShipped = 0x04;
    constexpr uint8_t kGateNever = 0x00;

    FCSE::Relocation<uint8_t*> g_plainGate{FCSE::Pattern(
        "8B 44 24 40 8B 4C 24 44 8B 54 24 18 89 43 10 89 4B 14 F6 82 9C 00 00 00 04 0F 84")};
    FCSE::Relocation<uint8_t*> g_instancedGate{FCSE::Pattern(
        "8B 54 24 30 8B 44 24 34 8B 4C 24 18 89 53 10 89 43 14 F6 81 9C 00 00 00 04 0F 84")};

    // CMarker::SetArchetypeByName(slot, name); the hook sits on its CMP EAX,9 after the slot load.
    FCSE::Relocation<uint8_t*> g_setArchetype{
        FCSE::Pattern("8B 44 24 04 83 F8 09 56 8B F1 77 38 8B 4C 24 0C 6A FF")};
    constexpr size_t kSlotCompare = 4;

    // Marker archetype slots: 5 archBlink, 6 archBlinkCompass, 9 archBlinkCompassVehicle. Any slot
    // past 9 is already refused by the function itself.
    constexpr uintptr_t kBlinkSlotMap = 5;
    constexpr uintptr_t kBlinkSlotCompass = 6;
    constexpr uintptr_t kBlinkSlotCompassVehicle = 9;
    constexpr uintptr_t kRefusedSlot = 10;

    // The archetype-reference visitors, dispatching through visitor +0x68 and +0xDC, each with the
    // offset of its own RET 8.
    FCSE::Relocation<uint8_t*> g_visit68{FCSE::Pattern(
        "8B C1 8B 50 10 8B 4C 24 08 56 8B 31 57 8D 3C D5 00 00 00 00 2B FA 8B 50 0C 8D 14 BA 03 54 "
        "24 0C 83 C0 04 52 50 8B 46 68 FF D0 5F 5E C2 08 00")};
    FCSE::Relocation<uint8_t*> g_visitDC{FCSE::Pattern(
        "8B C1 8B 50 10 8B 4C 24 08 56 8B 31 57 8D 3C D5 00 00 00 00 2B FA 8B 50 0C 8D 14 BA 03 54 "
        "24 0C 83 C0 04 52 50 8B 86 DC 00 00 00 FF D0 5F 5E C2 08 00")};
    constexpr size_t kVisit68Return = 0x2C;
    constexpr size_t kVisitDCReturn = 0x2F;

    // The property descriptor in ECX, its name at +4.
    constexpr uintptr_t kDescriptorName = 0x04;

    void WriteGates(bool never) {
        const uint8_t value = never ? kGateNever : kGateShipped;
        for (const auto* gate : {&g_plainGate, &g_instancedGate}) {
            if (*gate) {
                FCSE::ApiPointer()->Patch(gate->get() + kGateImmediate, &value, 1);
            }
        }
    }

    void SetArchetypeHandler(FCSE_MidHookContext* ctx) {
        if (!g_noBlinking.load(std::memory_order_relaxed)) {
            return;
        }

        if (ctx->eax == kBlinkSlotMap || ctx->eax == kBlinkSlotCompass ||
            ctx->eax == kBlinkSlotCompassVehicle) {
            ctx->eax = kRefusedSlot;
        }
    }

    bool IsBlinkProperty(uintptr_t descriptor) {
        const char* name = *reinterpret_cast<const char* const*>(descriptor + kDescriptorName);
        return name != nullptr &&
               (_stricmp(name, "archBlink") == 0 || _stricmp(name, "archBlinkCompass") == 0 ||
                _stricmp(name, "archBlinkCompassVehicle") == 0);
    }

    // At the entry nothing is pushed yet, so the function's own RET 8 returns cleanly.
    void SkipBlinkVisit(FCSE_MidHookContext* ctx, uintptr_t ret) {
        if (g_noBlinking.load(std::memory_order_relaxed) && IsBlinkProperty(ctx->ecx)) {
            ctx->eip = ret;
        }
    }

    void Visit68Handler(FCSE_MidHookContext* ctx) {
        SkipBlinkVisit(ctx, g_visit68.address() + kVisit68Return);
    }

    void VisitDCHandler(FCSE_MidHookContext* ctx) {
        SkipBlinkVisit(ctx, g_visitDC.address() + kVisitDCReturn);
    }
}

void InstallNoBlinkingItems() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_plainGate || !g_instancedGate) {
        FCSE::Logf("no blinking pickups: a highlight gate was not found in this build - pickups "
                   "may still pulse");
    }

    if (!g_setArchetype || !api->MidHook(g_setArchetype.get() + kSlotCompare, &SetArchetypeHandler)) {
        FCSE::Logf("no blinking pickups: CMarker::SetArchetypeByName could not be hooked - the "
                   "save-point disk still blinks");
    }

    const bool visit68 = g_visit68 && api->MidHook(g_visit68.get(), &Visit68Handler);
    const bool visitDC = g_visitDC && api->MidHook(g_visitDC.get(), &VisitDCHandler);
    if (!visit68 || !visitDC) {
        FCSE::Logf("no blinking pickups: an archetype visitor could not be hooked - markers given "
                   "a blink archetype in data may still blink");
    }
}

void __cdecl OnNoBlinkingItemsChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const bool on = value->asCheckbox;
    if (on == g_noBlinking.load(std::memory_order_relaxed)) {
        return;
    }

    g_noBlinking.store(on, std::memory_order_relaxed);
    WriteGates(on);
    FCSE::Logf(on ? "no blinking pickups: on" : "no blinking pickups: off - pickups pulse as shipped");
}
