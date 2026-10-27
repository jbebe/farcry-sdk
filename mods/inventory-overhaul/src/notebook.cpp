// The map gadget's strategy says when the map comes out and goes away; the game's signals, Tab's
// toggle_notebook and the map key's toggle_gadget, say which face it shows. The terminal hands one
// signal to each of its listeners in turn, the pause service's before the player's, so a signal
// rewritten in the pause service's is what the player's sees.
#include "notebook.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "page.h"

#include <atomic>

namespace InventoryOverhaul::Notebook {

namespace {
    using SignalFn = bool(__fastcall*)(void* dispatcher, void* unused, const uint32_t* signal,
                                       void* context);
    using EquipFn = void(__fastcall*)(void* strategy, void* unused);
    using UpdateFn = void(__fastcall*)(void* strategy, void* unused, float seconds);
    using GetWielderFn = void*(__fastcall*)(void* equipment, void* unused);

    // The pause service's listener, which sees every action map signal, found past its first
    // instruction because DevTools hooks its entry, and CGadgetMapStrategy's OnEquip, OnUnequip and
    // Update, which runs every frame while the map is out. None is an entry the address library
    // knows.
    FCSE::Relocation<uint8_t*> g_signalBody{
        FCSE::Pattern("83 EC 50 A8 01 53 55 56 57 8B F9 75 ?? 83 C8 01 A3")};
    FCSE::Relocation<EquipFn> g_equipSite{
        FCSE::Pattern("83 EC 10 53 55 56 8B F1 8B 4E 38 57 E8 ?? ?? ?? ?? 8B D8 85 DB 0F 84")};
    FCSE::Relocation<EquipFn> g_unequipSite{FCSE::Pattern(
        "83 EC 54 53 55 56 8B E9 8B 4D 38 57 E8 ?? ?? ?? ?? 33 DB 3B C3 89 44 24 14 0F 84")};
    FCSE::Relocation<UpdateFn> g_updateSite{FCSE::Pattern(
        "D9 44 24 04 83 EC 14 53 55 56 57 51 D9 1C 24 8B D9 E8 ?? ?? ?? ?? 8B 83 70 01 00 00 8B 30")};
    // CEquipmentBase::GetWielder: the pawn holding the map, which is its strategy's +0x38.
    FCSE::Relocation<GetWielderFn> g_getWielder{FCSE::Uplay(0x00156BA0)};
    constexpr ptrdiff_t kStrategyEquipment = 0x38;
    constexpr ptrdiff_t kSignalEntry = -5;
    SignalFn g_signal = nullptr;
    EquipFn g_equip = nullptr;
    EquipFn g_unequip = nullptr;
    UpdateFn g_update = nullptr;

    constexpr uint32_t kToggleNotebook = FCSE::Crc32("toggle_notebook");
    // The map key's signal, which Tab's becomes to bring the map out or put it away.
    constexpr uint32_t kToggleGadget = FCSE::Crc32("toggle_gadget");
    // A signal no listener knows, which one that only turns the map over becomes.
    constexpr uint32_t kTurned = FCSE::Crc32("InventoryOverhaul.Turned");
    // The mouse wheel's signal, which moves along the page while the notebook's face is up: how far
    // the wheel turned, and whether it fired this frame.
    constexpr uint32_t kCycleWeapon = FCSE::Crc32("cycleweapon");
    constexpr ptrdiff_t kSignalValue = 0x04;
    constexpr ptrdiff_t kSignalFired = 0x10;
    // R with the map out, which turns the notebook's page while its face is up.
    constexpr uint32_t kFlipSide = FCSE::Crc32("flipside");

    // Whether the map is out, whether its notebook face is up, and the face it comes out with.
    bool g_out = false;
    std::atomic<bool> g_notebook{false};
    bool g_askedNotebook = false;

    bool __fastcall OnSignal(void* dispatcher, void* unused, const uint32_t* signal, void* context) {
        if (signal != nullptr && *signal == kCycleWeapon && g_notebook) {
            const float turned = Read<float>(signal, kSignalValue);
            if (Read<uint32_t>(signal, kSignalFired) > 0 && turned != 0.0f) {
                Page::Scroll(turned > 0.0f ? -1 : 1);
            }
            *const_cast<uint32_t*>(signal) = kTurned;
        }
        if (signal != nullptr && *signal == kFlipSide && g_notebook) {
            Page::Turn();
            *const_cast<uint32_t*>(signal) = kTurned;
        }
        if (signal != nullptr && (*signal == kToggleNotebook || *signal == kToggleGadget)) {
            const bool notebook = *signal == kToggleNotebook;
            uint32_t& id = *const_cast<uint32_t*>(signal);
            if (g_out && g_notebook != notebook) {
                g_notebook = notebook;
                id = kTurned;
                FCSE::Logf("the map is turned to its %s face", notebook ? "notebook" : "map");
            } else {
                if (!g_out) {
                    g_askedNotebook = notebook;
                }
                id = kToggleGadget;
            }
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

    // The page is kept up to date from the map's own frame, with the inventory of the pawn holding
    // it.
    void __fastcall OnUpdate(void* strategy, void* unused, float seconds) {
        g_update(strategy, unused, seconds);
        if (!g_notebook) {
            return;
        }
        void* pawn = g_getWielder(Read<void*>(strategy, kStrategyEquipment), nullptr);
        if (pawn != nullptr) {
            const void* view = Inventory::ViewOf(pawn);
            Page::Update(view, Inventory::OfView(view));
        }
    }
}

bool Install() {
    const bool hooked =
        g_signalBody && g_equipSite && g_unequipSite &&
        FCSE::ApiPointer()->Hook(g_signalBody.get() + kSignalEntry, reinterpret_cast<void*>(&OnSignal),
                                 reinterpret_cast<void**>(&g_signal)) &&
        Hook(g_equipSite, &OnEquip, &g_equip) && Hook(g_unequipSite, &OnUnequip, &g_unequip);
    const bool paged = hooked && g_getWielder && Hook(g_updateSite, &OnUpdate, &g_update);
    FCSE::Logf("notebook: %s", !hooked ? "off, the map or the signals were not found"
                               : paged ? "on Tab"
                                       : "on Tab, but the page is not kept up: the map's update "
                                         "was not found");
    return hooked;
}

bool IsOpen() { return g_notebook; }

}
