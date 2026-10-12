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

    constexpr Group kGroups[] = {{"Land", kLand}, {"Water", kWater}, {"Air", kAir}};
}

namespace DevTools::Spawnables {

std::span<const Group> All() { return kGroups; }

}
