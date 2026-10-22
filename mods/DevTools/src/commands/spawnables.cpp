#include "commands/spawnables.h"

namespace {
    using DevTools::Spawnables::Group;
    using DevTools::Spawnables::Spawnable;

    // Single-player vehicles, without their Multi, Scripted, Broken and Stuck variants.
    constexpr Spawnable kLand[] = {
        {"Datsun", "vehicle.Land.Datsun"},
        {"Jeep Liberty", "vehicle.Land.JeepLiberty"},
        {"Jeep Wrangler", "vehicle.Land.JeepWrangler"},
        {"Buggy", "vehicle.Land.Buggy"},
        {"Rover", "vehicle.Land.Rover"},
        {"Rover, M249", "vehicle.Land.Rover.M249_Mounted"},
        {"Rover, M2", "vehicle.Land.Rover.M2_Mounted"},
        {"Rover, MK19", "vehicle.Land.Rover.MK19_Mounted"},
        {"Big truck", "vehicle.Land.BigTruck"},
        {"Tanker", "vehicle.Land.BigTruck.Tanker"},
    };

    constexpr Spawnable kWater[] = {
        {"Fishing boat", "vehicle.Sea.FishingBoat"},
        {"Fishing boat, M249", "vehicle.Sea.FishingBoat.M249_Mounted"},
        {"Fishing boat, M2", "vehicle.Sea.FishingBoat.M2_Mounted"},
        {"Fishing boat, MK19", "vehicle.Sea.FishingBoat.MK19_Mounted"},
        {"Swamp boat", "vehicle.Sea.SwampBoat"},
        {"Swamp boat, M249", "vehicle.Sea.SwampBoat.M249_Mounted"},
        {"Swamp boat, M2", "vehicle.Sea.SwampBoat.M2_Mounted"},
        {"Swamp boat, MK19", "vehicle.Sea.SwampBoat.MK19_Mounted"},
        {"Gunboat", "vehicle.Sea.GunBoat"},
    };

    constexpr Spawnable kAir[] = {
        {"Hang glider", "vehicle.Air.Paraglider"},
    };

    // The Unimog carries an M2 as it stands; its other guns exist only as Multi variants.
    constexpr Spawnable kDlc[] = {
        {"ATV", "vehicle.Land.DLC_Vehicle1_DLC1"},
        {"Unimog, M2", "vehicle.Land.DLC_Vehicle2_DLC1"},
        {"Unimog, M249", "vehicle.Land.DLC_Vehicle2_DLC1.Multi_M249_Mounted"},
        {"Unimog, MK19", "vehicle.Land.DLC_Vehicle2_DLC1.Multi_MK19_Mounted"},
    };

// Each class of a faction as a Caucasian and a Nubian.
#define DEVTOOLS_SOLDIER(faction, label, soldierClass)                                             \
    {label ", Caucasian", "enemy_archetypes." faction "." soldierClass "_Caucasian"},              \
        {label ", Nubian", "enemy_archetypes." faction "." soldierClass "_Nubian"}
#define DEVTOOLS_FACTION(faction)                                                                  \
    DEVTOOLS_SOLDIER(faction, "Assault", "Assault"),                                               \
        DEVTOOLS_SOLDIER(faction, "Shotgun", "ShotgunMan"),                                        \
        DEVTOOLS_SOLDIER(faction, "Sniper", "Sniper"),                                             \
        DEVTOOLS_SOLDIER(faction, "Machine gun", "LightMachineGunner"),                            \
        DEVTOOLS_SOLDIER(faction, "RPG", "RocketMan"),                                             \
        DEVTOOLS_SOLDIER(faction, "Carl Gustaf", "CarlGustaf"),                                    \
        DEVTOOLS_SOLDIER(faction, "Mortar", "MortarMan")

    // The campaign worlds' libraries lack some of these; testrange's, tmpla's, has them all.
    constexpr Spawnable kRed[] = {DEVTOOLS_FACTION("Red_Faction")};
    constexpr Spawnable kBlue[] = {DEVTOOLS_FACTION("Blue_Faction")};

#undef DEVTOOLS_FACTION
#undef DEVTOOLS_SOLDIER

    // Ubisoft's own test soldiers, in tmpla's library only.
    constexpr Spawnable kMercTest[] = {
        {"Generic", "enemy_archetypes.MercTest.Generic"},
        {"Aim test", "enemy_archetypes.MercTest.AimTest"},
        {"Target practice", "enemy_archetypes.MercTest.Target_Practice"},
        {"Movement test", "enemy_archetypes.MercTest.MovementTest"},
        {"Health test", "enemy_archetypes.MercTest.HealthTest"},
        {"Lean test", "enemy_archetypes.MercTest.LeanTest"},
    };

    constexpr Group kGroups[] = {
        {"Land", kLand},
        {"Water", kWater},
        {"Air", kAir},
        {"DLC", kDlc},
        {"Red faction", kRed, true},
        {"Blue faction", kBlue, true},
        {"Test soldiers", kMercTest, true},
    };
}

namespace DevTools::Spawnables {

std::span<const Group> All() { return kGroups; }

}
