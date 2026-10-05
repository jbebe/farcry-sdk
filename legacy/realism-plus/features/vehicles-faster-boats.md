---
title: Faster boats
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/sea/**#**/BoatParams/*"
exclude: []
requires: []
verified: diff
---

# Faster boats

Swamp boats and fishing boats push about 10-15 % harder forward and in reverse, and brake as much
harder.

## How

`CVehicleFloatingPhysComponent/BoatParams` on the boat copies the mod adds to
`generated/entitylibrarypatchoverride.fcb` (redeclaring `worlds/world1` or `worlds/world2`), 18
values:

| Boat | `fForwardEnginePower` | `fReverseEnginePower` | `fEngineBrakingPower` |
|---|---|---|---|
| `Sea.SwampBoat`, `.M2_Mounted`, `.M249_Mounted`, `.MK19_Mounted` | 2.1 -> 2.3 | 2 -> 2.3 | 2 -> 2.3 |
| `Sea.FishingBoat`, `.M249_Mounted` | 3.5 -> 4 | 5 -> 5.8 | 4 -> 4.6 |

The scripted boats (`Sea.SwampBoat.ScriptedSwampBoat`, `Sea.FishingBoat.ScriptedFishingBoat`) and
`Sea.GunBoat` keep vanilla values. This is the "Boats" recipe of Boggalog's guide
([vehicles](../../../docs/docs/modding/guide/vehicles.md#boats)).
