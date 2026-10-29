// Other mods' papers and items, such as mods\tales-of-africa's, are such pickups.
#include "pickups.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "items.h"

#include <cstdlib>

namespace InventoryOverhaul::Pickups {

namespace {
    // CPickupMissionItem::Pick, given its picker's proxy by value, which it releases.
    using PickFn = char(__fastcall*)(void* pickup, void* unused, void* picker, bool flag);

    FCSE::Relocation<PickFn> g_pickSite{
        FCSE::Pattern("8B 44 24 08 83 EC 40 53 56 57 8B 7C 24 50 50 51 8B C4 89 38 83 47 08 01 8B F1 "
                      "E8 ?? ?? ?? ?? 8B 8E C8 00 00 00 33 DB 88 5E 15 88 9E D1 00 00 00")};
    PickFn g_pick = nullptr;

    // A cassette's sound event is written as the game writes one, such as "0x004E1C52".
    char __fastcall OnPick(void* pickup, void* unused, void* picker, bool flag) {
        const char picked = g_pick(pickup, unused, picker, flag);
        const auto* data = FCSE::ApiPointer()->EntityData;
        void* entity = Inventory::Entity(pickup);
        char id[64];
        char sound[16] = "0";
        if (data->GetString(entity, "InventoryOverhaul.Item", id, sizeof id)) {
            data->GetString(entity, "InventoryOverhaul.Sound", sound, sizeof sound);
            Items::Give(id, std::strtoul(sound, nullptr, 16));
        }
        return picked;
    }
}

bool Install() {
    const bool hooked = Hook(g_pickSite, &OnPick, &g_pick);
    FCSE::Logf("pickups: %s", hooked ? "papers and items picked up go into Items"
                                     : "Pick was not found, so papers and items only vanish");
    return hooked;
}

}
