// The map gadget's strategy says when the map comes out and goes away; the game's signals, Tab's
// toggle_notebook and the map key's toggle_gadget, say which face it shows.
#include "notebook.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <atomic>

namespace InventoryOverhaul::Notebook {

namespace {
    using SignalFn = bool(__fastcall*)(void* dispatcher, void* unused, const uint32_t* signal,
                                       void* context);
    using EquipFn = void(__fastcall*)(void* strategy, void* unused);

    // The dispatcher every action map signal goes through, found past its first instruction
    // because DevTools hooks its entry, and CGadgetMapStrategy's OnEquip and OnUnequip. None is an
    // entry the address library knows.
    FCSE::Relocation<uint8_t*> g_signalBody{
        FCSE::Pattern("83 EC 50 A8 01 53 55 56 57 8B F9 75 ?? 83 C8 01 A3")};
    FCSE::Relocation<EquipFn> g_equipSite{
        FCSE::Pattern("83 EC 10 53 55 56 8B F1 8B 4E 38 57 E8 ?? ?? ?? ?? 8B D8 85 DB 0F 84")};
    FCSE::Relocation<EquipFn> g_unequipSite{FCSE::Pattern(
        "83 EC 54 53 55 56 8B E9 8B 4D 38 57 E8 ?? ?? ?? ?? 33 DB 3B C3 89 44 24 14 0F 84")};
    constexpr ptrdiff_t kSignalEntry = -5;
    SignalFn g_signal = nullptr;
    EquipFn g_equip = nullptr;
    EquipFn g_unequip = nullptr;

    constexpr uint32_t kToggleNotebook = FCSE::Crc32("toggle_notebook");
    // The map key's signal, also sent in Tab's place to bring the map out or put it away.
    constexpr uint32_t kToggleGadget = FCSE::Crc32("toggle_gadget");
    const uint32_t g_toggleGadget = kToggleGadget;

    // Whether the map is out, whether its notebook face is up, and the face it comes out with.
    bool g_out = false;
    std::atomic<bool> g_notebook{false};
    bool g_askedNotebook = false;

    // True, as the dispatcher's own refusal is, for a signal that only turns the map over.
    bool Turn(bool notebook) {
        g_notebook = notebook;
        FCSE::Logf("the map is turned to its %s face", notebook ? "notebook" : "map");
        return true;
    }

    bool __fastcall OnSignal(void* dispatcher, void* unused, const uint32_t* signal, void* context) {
        if (signal != nullptr && (*signal == kToggleNotebook || *signal == kToggleGadget)) {
            const bool notebook = *signal == kToggleNotebook;
            if (g_out && g_notebook != notebook) {
                return Turn(notebook);
            }
            if (!g_out) {
                g_askedNotebook = notebook;
            }
            return g_signal(dispatcher, unused, &g_toggleGadget, context);
        }
        return g_signal(dispatcher, unused, signal, context);
    }

    void __fastcall OnEquip(void* strategy, void* unused) {
        g_equip(strategy, unused);
        g_out = true;
        g_notebook = g_askedNotebook;
        g_askedNotebook = false;
        FCSE::Logf("the map is out, %s face up", g_notebook ? "notebook" : "map");
    }

    void __fastcall OnUnequip(void* strategy, void* unused) {
        g_unequip(strategy, unused);
        g_out = false;
        g_notebook = false;
    }
}

bool Install() {
    const bool hooked =
        g_signalBody && g_equipSite && g_unequipSite &&
        FCSE::ApiPointer()->Hook(g_signalBody.get() + kSignalEntry, reinterpret_cast<void*>(&OnSignal),
                                 reinterpret_cast<void**>(&g_signal)) &&
        Hook(g_equipSite, &OnEquip, &g_equip) && Hook(g_unequipSite, &OnUnequip, &g_unequip);
    FCSE::Logf("notebook: %s", hooked ? "on Tab" : "off, the map or the signals were not found");
    return hooked;
}

bool IsOpen() { return g_notebook; }

}
