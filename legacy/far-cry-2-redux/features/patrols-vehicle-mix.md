---
title: Patrols on DLC quads and Unimogs
kind: component
bundle: gameplay
status: located
systems: [patrols, vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/ghostpatrols/**#Entity/Ghost/archVehicle"
exclude: []
requires: []
verified: diff
---

# Patrols on DLC quads and Unimogs

The Datsun patrol rides a quad bike, and the Jeep patrols drive the DLC Unimog truck.

## How

`Ghost/archVehicle` in the patrol copies of `generated/entitylibrarypatchoverride.fcb/ghostpatrols/`,
3 values:

| Patrol (`GhostPatrols.Patrols.…`) | World | Vanilla | Mod |
|---|---|---|---|
| `Datsun` | both | `vehicle.Land.Datsun` | `vehicle.Land.DLC_Vehicle1_DLC1` (quad) |
| `JeepWrangler` | both | `vehicle.Land.JeepWrangler` | `vehicle.Land.DLC_Vehicle2_DLC1` (Unimog) |
| `JeepLiberty` | 2 | `vehicle.Land.JeepLiberty` | `vehicle.Land.DLC_Vehicle2_DLC1` (Unimog) |

The two vehicles are the campaign DLC versions in `downloadcontent/dlc1/generated/entitylibrary.fcb`,
which the game reads them from; the mod's own copies of them in the override library are dead
(`noise-world-dlc-placeholder-copies`). This is the "Vehicle type" patrol recipe of Boggalog's
guide ([patrols](../../../docs/docs/modding/guide/patrols.md#vehicle-type)).

## Depends on

The DLC vehicle library (every current release has it). The Wrangler patrol is red-crewed with a
second seat (`patrols-red-faction-crews`) and the Liberty patrol gets a shotgunner
(`patrols-blue-crews`); the Datsun patrol keeps one rider.

## Compared with Realism Plus

[`patrols-vehicle-mix`](../../realism-plus/features/patrols-vehicle-mix.md) also moves patrols onto
DLC Unimogs, but the multiplayer gun-mounted variants, and puts the buggy on the Wrangler patrol.

## Uncertain

- How patrol drivers handle the quad is not checked in game.
