#include "gps.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace MapOverhaul::Gps {

namespace {
    // CGadgetMapStrategy's EquipGadgetItem and UnequipGadgetItem(TEntityHandle, CPawn*), which put the
    // map's compass or monocular in the hand and take it out again. The handle comes by value: the caller
    // adds a reference and the callee releases it.
    using GadgetItemFn = void(__fastcall*)(void* strategy, void* unused, void* item, void* pawn);
    // CVehicle::SVehicleImpl::AttachToSeat(seat, entry, pawn), which for the player binds his vehicle GPS
    // to the dashboard and shows it.
    using AttachToSeatFn = void(__fastcall*)(void* vehicle, void* unused, void* seat, uint32_t entry, void* pawn);
    using SetVisibleFn = void(__fastcall*)(void* entity, void* unused, bool visible);

    FCSE::Relocation<GadgetItemFn> g_equipSite{FCSE::Pattern(
        "53 56 8B 74 24 0C 57 8B 7E 0C 8B D9 8B CF E8 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8 ?? ?? "
        "?? ?? 68 ?? ?? ?? ?? 8B CF E8")};
    FCSE::Relocation<GadgetItemFn> g_unequipSite{FCSE::Pattern(
        "55 8B EC 83 E4 F0 83 EC 14 53 56 8B 75 08 57 8B 7E 0C 8B CF E8 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 00 75 "
        "07 33 C9 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8B CF E8 ?? ?? ?? ?? 8B 5D 0C 53 8B C8 E8")};
    FCSE::Relocation<AttachToSeatFn> g_attachSite{FCSE::Pattern(
        "55 8B EC 83 E4 F0 83 EC 54 53 56 8B F1 8B 4E 10 33 C0 85 C9 57 89 74 24 10 76 18 8B 56 0C 8B FF 8B "
        "7D 08 3B D7")};
    // CEntity::SetIsVisible(bool).
    FCSE::Relocation<SetVisibleFn> g_setVisible{FCSE::Pattern(
        "53 8B 5C 24 08 33 C0 8A E3 56 8B F1 57 33 FF 33 46 70 25 00 01 00 00 31 46 70")};
    GadgetItemFn g_equip = nullptr;
    GadgetItemFn g_unequip = nullptr;
    AttachToSeatFn g_attach = nullptr;

    // The map strategy's CompassEntity handle, and a handle's reference count and entity.
    constexpr ptrdiff_t kStrategyCompass = 0x108;
    constexpr ptrdiff_t kHandleReferences = 0x08;
    constexpr ptrdiff_t kHandleEntity = 0x0C;
    // CPawn::GetInventoryView is the pawn's +0x10 plus 0x4F0; its implementation holds the vehicle GPS.
    constexpr ptrdiff_t kPawnParts = 0x10;
    constexpr ptrdiff_t kPartsInventoryView = 0x4F0;
    constexpr ptrdiff_t kViewImplementation = 0x04;
    constexpr ptrdiff_t kImplementationVehicleGps = 0x54;

    template <class T>
    T Read(const void* object, ptrdiff_t offset) {
        return *reinterpret_cast<const T*>(static_cast<const uint8_t*>(object) + offset);
    }

    template <class Fn>
    bool Hook(const FCSE::Relocation<Fn>& site, Fn detour, Fn* original) {
        return site && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                                reinterpret_cast<void*>(detour),
                                                reinterpret_cast<void**>(original));
    }

    // The compass is left in the pocket: the reference the caller added is released as the callee would,
    // and the map keeps its own.
    bool LeftInPocket(void* strategy, void* item) {
        if (item != Read<void*>(strategy, kStrategyCompass)) {
            return false;
        }
        --*reinterpret_cast<int32_t*>(static_cast<uint8_t*>(item) + kHandleReferences);
        return true;
    }

    void __fastcall OnEquipItem(void* strategy, void* unused, void* item, void* pawn) {
        if (!LeftInPocket(strategy, item)) {
            g_equip(strategy, unused, item, pawn);
        }
    }

    void __fastcall OnUnequipItem(void* strategy, void* unused, void* item, void* pawn) {
        if (!LeftInPocket(strategy, item)) {
            g_unequip(strategy, unused, item, pawn);
        }
    }

    void __fastcall OnAttachToSeat(void* vehicle, void* unused, void* seat, uint32_t entry, void* pawn) {
        g_attach(vehicle, unused, seat, entry, pawn);
        const uint8_t* view = Read<uint8_t*>(pawn, kPawnParts) + kPartsInventoryView;
        const void* gps = Read<void*>(Read<void*>(view, kViewImplementation), kImplementationVehicleGps);
        if (void* entity = Read<void*>(gps, kHandleEntity)) {
            g_setVisible(entity, nullptr, false);
        }
    }
}

bool Install() {
    const bool hand = Hook(g_equipSite, &OnEquipItem, &g_equip) && Hook(g_unequipSite, &OnUnequipItem, &g_unequip);
    const bool dashboard = g_setVisible && Hook(g_attachSite, &OnAttachToSeat, &g_attach);
    FCSE::Logf("GPS: %s in the hand, %s on the dashboard", hand ? "none" : "still", dashboard ? "none" : "still");
    return hand || dashboard;
}

}
