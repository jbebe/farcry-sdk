---
title: Enemies fill every seat of a patrol vehicle
kind: component
bundle: gameplay
claims:
  - "Enemies can now occupy every seat within a vehicle"
status: located
systems: [patrols, vehicles, ai]
match:
  - "**/entitylibrary*.fcb/ghostpatrols/**#Entity/Ghost/Passengers/Passenger[+*]"
  - "install/bin/dunia.dll@0xed68ac"
exclude: []
requires: []
verified: re
---

# Enemies fill every seat of a patrol vehicle

Patrol and convoy vehicles spawn with a crew member for each seat instead of one or two.

## How

New `Passenger` entries in the `Ghost/Passengers` list of the vanilla patrol archetypes in
`worlds/world1` and `worlds/world2` `entitylibrary.fcb/ghostpatrols/` (21 additions):

- `Patrols.Datsun`, `Rover`, `Rover.M249_Mounted`: one more each, in both worlds.
- `Patrols.Rover.M2_Mounted`, `Rover.MK19_Mounted` (world2): one more each.
- `Patrols.JeepWrangler` (both worlds) and `JeepLiberty` (world2): three more each.
- `Convoy.ConvoyTarget`: one more (`Blue_Faction.Assault_Nubian`); `Convoy.EscortVehicle`: one more
  (world1 `RocketMan_Caucasian`, world2 `ShotgunMan_Caucasian`), which fits the Unimog it now is
  (`convoy-unimog-escorts`).

The added crew already carry the mixed weapon classes of `patrol-diverse-weapons`; the new
archetypes of `randomized-patrols-core` and `faction-conflicts` are written with full crews from
the start.

## Dunia.dll

The string `passengerPriority` in `.rdata` is overwritten with `gunnerPriority\0\0\0`. Four functions
read that exact pointer: the load and parameter-description functions of the AI tasks
`CTaskVehicleSetUserRolePriority` and `CTaskVehicleCheckUserPriority`. Every such task therefore
reads its passenger priority (None/Last/Second/First) from the authored `gunnerPriority` value, so
passenger seats fill the way gunner seats do (inferred from the readers). The vehicle brain edit
that goes with it is on `fewer-vehicle-chases`.

| | Steam | GOG |
|---|---|---|
| string | `0x10ED68AC` | `0x10E4DE74` |

Pattern (one match in each build, the string itself):
`70 61 73 73 65 6E 67 65 72 50 72 69 6F 72 69 74 79 00`

## Uncertain

- Whether the extra passengers sit in the vehicle without the `Dunia.dll` edit (vanilla
  `passengerPriority` is `0` in most of the vehicle brain's role blocks) is not checked in game.
