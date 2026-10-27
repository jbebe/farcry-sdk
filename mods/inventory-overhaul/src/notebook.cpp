// The terminal hands one signal to each of its listeners in turn, the pause service's before the
// player's, so a signal rewritten in the pause service's is what the player's sees.
#include "notebook.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "page.h"

#include <atomic>
#include <utility>

namespace InventoryOverhaul::Notebook {

namespace {
    using SignalFn = bool(__fastcall*)(void* dispatcher, void* unused, const uint32_t* signal,
                                       void* context);
    using EquipFn = void(__fastcall*)(void* strategy, void* unused);
    using UpdateFn = void(__fastcall*)(void* strategy, void* unused, float seconds);
    using GetWielderFn = void*(__fastcall*)(void* equipment, void* unused);
    using ToggleGadgetFn = void(__fastcall*)(void* action, void* unused, void* pawn);
    using LoadSlotsFn = void(__fastcall*)(void* service, void* unused, void* config);
    using InsertSlotFn = void(__fastcall*)(void* slots, void* unused, void* inserted,
                                           const uint32_t* slot);

    // The pause service's listener, which sees every action map signal, found past its first
    // instruction because DevTools hooks its entry, and CGadgetMapStrategy's OnEquip, OnUnequip and
    // Update, which runs every frame while a map is out. None is an entry the address library
    // knows.
    FCSE::Relocation<uint8_t*> g_signalBody{
        FCSE::Pattern("83 EC 50 A8 01 53 55 56 57 8B F9 75 ?? 83 C8 01 A3")};
    FCSE::Relocation<EquipFn> g_equipSite{
        FCSE::Pattern("83 EC 10 53 55 56 8B F1 8B 4E 38 57 E8 ?? ?? ?? ?? 8B D8 85 DB 0F 84")};
    FCSE::Relocation<EquipFn> g_unequipSite{FCSE::Pattern(
        "83 EC 54 53 55 56 8B E9 8B 4D 38 57 E8 ?? ?? ?? ?? 33 DB 3B C3 89 44 24 14 0F 84")};
    FCSE::Relocation<UpdateFn> g_updateSite{FCSE::Pattern(
        "D9 44 24 04 83 EC 14 53 55 56 57 51 D9 1C 24 8B D9 E8 ?? ?? ?? ?? 8B 83 70 01 00 00 8B 30")};
    // CEquipmentBase::GetWielder: the pawn holding the gadget, which is its strategy's +0x38.
    FCSE::Relocation<GetWielderFn> g_getWielder{FCSE::Uplay(0x00156BA0)};
    // CGOStateEventInventory::ToggleGadget(pawn), the map key's action, and the map's category it
    // reads.
    FCSE::Relocation<ToggleGadgetFn> g_toggleGadgetSite{FCSE::Uplay(0x00CE2440)};
    FCSE::Relocation<uint32_t*> g_mapCategory{FCSE::Uplay(0x00FCD628)};
    // CFCXWeaponsService::LoadCategorySlots, which fills its std::map of gadget categories to slots
    // from gamemodesconfig.xml, and that map's insert. A gadget of a category without a slot can be
    // carried but not taken out.
    FCSE::Relocation<LoadSlotsFn> g_loadSlotsSite{FCSE::Uplay(0x007692F0)};
    FCSE::Relocation<InsertSlotFn> g_insertSlot{FCSE::Uplay(0x0029EAB0)};
    constexpr ptrdiff_t kServiceGadgetSlots = 0x13C;
    constexpr ptrdiff_t kStrategyEquipment = 0x38;
    constexpr ptrdiff_t kSignalEntry = -5;
    SignalFn g_signal = nullptr;
    EquipFn g_equip = nullptr;
    EquipFn g_unequip = nullptr;
    UpdateFn g_update = nullptr;
    ToggleGadgetFn g_toggleGadget = nullptr;
    LoadSlotsFn g_loadSlots = nullptr;

    constexpr const char* kArchetype = "gadgets.Equipped.Notebook";
    constexpr uint32_t kMap = FCSE::Crc32("map");
    // The notebook's category, the spare unused1, and its slot, the one after the map's.
    constexpr uint32_t kNotebook = FCSE::Crc32("unused1");
    constexpr uint32_t kNotebookSlot = 2;

    constexpr uint32_t kToggleNotebook = FCSE::Crc32("toggle_notebook");
    // The map key's signal, which Tab's becomes.
    constexpr uint32_t kToggleGadget = FCSE::Crc32("toggle_gadget");
    // A signal no listener knows, which one the notebook takes for itself becomes.
    constexpr uint32_t kTaken = FCSE::Crc32("InventoryOverhaul.Taken");
    // The mouse wheel's signal, which moves along the page while the notebook is out: how far the
    // wheel turned, and whether it fired this frame.
    constexpr uint32_t kCycleWeapon = FCSE::Crc32("cycleweapon");
    constexpr ptrdiff_t kSignalValue = 0x04;
    constexpr ptrdiff_t kSignalFired = 0x10;
    // R, which turns the notebook's page.
    constexpr uint32_t kFlipSide = FCSE::Crc32("flipside");

    // Whether the notebook is out, whether the toggle the player handles next is Tab's, and whether
    // the notebook could not be made.
    std::atomic<bool> g_open{false};
    bool g_tabbed = false;
    bool g_refused = false;
    // The player's inventory and how many items it held when last looked at for the notebook.
    const void* g_lookedAt = nullptr;
    size_t g_lookedAtItems = 0;

    bool IsNotebook(void* strategy) {
        return *Inventory::Category(Read<void*>(strategy, kStrategyEquipment)) == kNotebook;
    }

    bool __fastcall OnSignal(void* dispatcher, void* unused, const uint32_t* signal, void* context) {
        if (signal == nullptr) {
            return g_signal(dispatcher, unused, signal, context);
        }
        uint32_t& id = *const_cast<uint32_t*>(signal);
        switch (id) {
        case kCycleWeapon:
            if (g_open) {
                const float turned = Read<float>(signal, kSignalValue);
                if (Read<uint32_t>(signal, kSignalFired) > 0 && turned != 0.0f) {
                    Page::Scroll(turned > 0.0f ? -1 : 1);
                }
                id = kTaken;
            }
            break;
        case kFlipSide:
            if (g_open) {
                Page::Turn();
                id = kTaken;
            }
            break;
        case kToggleNotebook:
        case kToggleGadget:
            g_tabbed = id == kToggleNotebook;
            id = kToggleGadget;
            break;
        }
        return g_signal(dispatcher, unused, signal, context);
    }

    void __fastcall OnLoadSlots(void* service, void* unused, void* config) {
        g_loadSlots(service, unused, config);
        const uint32_t slot[] = {kNotebook, kNotebookSlot};
        uint32_t inserted[2];
        g_insertSlot(static_cast<uint8_t*>(service) + kServiceGadgetSlots, nullptr, inserted, slot);
    }

    // Tab's toggle is the map key's, run with the notebook's category in the map's place.
    void __fastcall OnToggleGadget(void* action, void* unused, void* pawn) {
        if (!std::exchange(g_tabbed, false)) {
            return g_toggleGadget(action, unused, pawn);
        }
        uint32_t& category = *g_mapCategory.get();
        const uint32_t map = std::exchange(category, kNotebook);
        g_toggleGadget(action, unused, pawn);
        category = map;
    }

    void __fastcall OnEquip(void* strategy, void* unused) {
        g_equip(strategy, unused);
        if (IsNotebook(strategy)) {
            g_open = true;
            FCSE::Logf("the notebook is out");
        }
    }

    void __fastcall OnUnequip(void* strategy, void* unused) {
        g_unequip(strategy, unused);
        if (IsNotebook(strategy)) {
            g_open = false;
        }
    }

    // The page is kept up to date from the notebook's own frame, with the inventory of the pawn
    // holding it.
    void __fastcall OnUpdate(void* strategy, void* unused, float seconds) {
        g_update(strategy, unused, seconds);
        if (!g_open || !IsNotebook(strategy)) {
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
        Inventory::Install() && g_signalBody && g_equipSite && g_unequipSite && g_mapCategory &&
        g_insertSlot && Hook(g_loadSlotsSite, &OnLoadSlots, &g_loadSlots) &&
        FCSE::ApiPointer()->Hook(g_signalBody.get() + kSignalEntry, reinterpret_cast<void*>(&OnSignal),
                                 reinterpret_cast<void**>(&g_signal)) &&
        Hook(g_toggleGadgetSite, &OnToggleGadget, &g_toggleGadget) &&
        Hook(g_equipSite, &OnEquip, &g_equip) && Hook(g_unequipSite, &OnUnequip, &g_unequip);
    const bool paged = hooked && g_getWielder && Hook(g_updateSite, &OnUpdate, &g_update);
    FCSE::Logf("notebook: %s", !hooked ? "off, the map or the signals were not found"
                               : paged ? "on Tab"
                                       : "on Tab, but the page is not kept up: the map's update "
                                         "was not found");
    return hooked;
}

void Frame() {
    if (g_refused) {
        return;
    }
    void* pawn = Inventory::PlayerPawn();
    if (pawn == nullptr) {
        return;
    }
    const void* view = Inventory::ViewOf(pawn);
    const size_t items = Inventory::Count(Inventory::OfView(view));
    if (view == g_lookedAt && items == g_lookedAtItems) {
        return;
    }
    g_lookedAt = view;
    g_lookedAtItems = items;
    if (Inventory::Gadget(view, kMap) == nullptr || Inventory::Gadget(view, kNotebook) != nullptr) {
        return;
    }
    g_refused = Inventory::AddGadget(Inventory::OfView(view), kArchetype, 1) == nullptr;
    FCSE::Logf(g_refused ? "the player has a map, but %s could not be made"
                         : "the player has a map: %s given",
               kArchetype);
}

bool IsOpen() { return g_open; }

}
