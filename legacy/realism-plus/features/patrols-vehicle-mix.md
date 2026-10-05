---
title: Patrols and convoy escorts drive other vehicles
kind: component
bundle: gameplay
status: located
systems: [patrols, vehicles]
match:
  - "worlds/*/generated/entitylibrary.fcb/ghostpatrols/**#Entity/Ghost/archVehicle"
exclude: []
requires: []
verified: diff
---

# Patrols and convoy escorts drive other vehicles

Patrols show up in a wider range of vehicles - buggies, the DLC Unimog with mounted guns - and convoy
escorts are armed.

## How

`Ghost/archVehicle` in the vanilla patrol archetypes of `worlds/world1` and `worlds/world2`
`entitylibrary.fcb/ghostpatrols/` (9 values):

| Archetype | World | Vanilla | Mod |
|---|---|---|---|
| `Patrols.JeepWrangler` | both | `Land.JeepWrangler` | `Land.Buggy` |
| `Patrols.Datsun` | 2 | `Land.Datsun` | `Land.JeepWrangler` |
| `Patrols.JeepLiberty` | 2 | `Land.JeepLiberty` | `Land.DLC_Vehicle2_DLC1.Multi_MK19_Mounted` |
| `Patrols.Rover.M249_Mounted` | both | `Land.Rover.M249_Mounted` | `Land.DLC_Vehicle2_DLC1.Multi_M249_Mounted` |
| `Patrols.SwampBoat.M249_Mounted` | 1 | `Sea.SwampBoat.M249_Mounted` | `Sea.SwampBoat.M2_Mounted` |
| `Convoy.EscortVehicle` | 1 | `Land.Rover` | `Land.Rover.M2_Mounted` |
| `Convoy.EscortVehicle` | 2 | `Land.Rover` | `Land.DLC_Vehicle2_DLC1` |

`DLC_Vehicle2_DLC1` is the Unimog; its `Multi_M249_Mounted` and `Multi_MK19_Mounted` variants are
the multiplayer versions with a mounted M249 or MK19, which ship in
`downloadcontent/dlc1/generated/entitylibrary.fcb`. They carry `selVehicleColor` `30` instead of the
campaign Unimog's `35`, so these patrols also bring Unimogs in another paint (read from the DLC
library). This is the "Vehicle type" patrol recipe of Boggalog's guide
([patrols](../../../docs/docs/modding/guide/patrols.md#vehicle-type)).

## Depends on

The DLC vehicle library must load (every install that has the DLC content). `vehicles-faster-land`
and `vehicles-collision-damage` also retune the two Unimog variants these patrols use (their
multiplayer `nMaxStimCollisionLevel` of `2` becomes `23`).

## Compared with Scubrah's Patch

[`convoy-unimog-escorts`](../../scubrahs-patch/features/convoy-unimog-escorts.md) puts the Unimog
on convoy escorts in both worlds; [`randomized-patrols-core`](../../scubrahs-patch/features/randomized-patrols-core.md)
rolls a different vehicle per patrol every few minutes. Here each patrol type gets one fixed
replacement.

## Uncertain

- That the multiplayer Unimog variants behave in the campaign (seat roles, gunner use) as the
  campaign vehicles do is not checked in game.
