---
title: Enemy infighting - patrols crewed by the other faction
kind: component
bundle: gameplay
status: located
systems: [patrols, ai]
match:
  - "worlds/world1/generated/entitylibrary.fcb/ghostpatrols/patrols/{rover,rover/m249_mounted,swampboat,swampboat/m249_mounted}.xml#Entity/Ghost/Passengers/Passenger[*]/archPassenger"
  - "worlds/world2/generated/entitylibrary.fcb/ghostpatrols/patrols/{jeepliberty,rover,rover/m249_mounted,rover/m2_mounted,rover/mk19_mounted,swampboat/m249_mounted,swampboat/mk19_mounted}.xml#Entity/Ghost/Passengers/Passenger[*]/archPassenger"
exclude: []
requires: [patrols-drivers]
verified: diff
---

# Enemy infighting - patrols crewed by the other faction

The armed patrols - Land Rovers, mounted-gun trucks and swamp boats - are crewed by the other
faction, so they trade fire with the guard posts and the other patrols they meet.

## How

In the vanilla patrol archetypes of `worlds/world1` and `worlds/world2`
`entitylibrary.fcb/ghostpatrols/patrols/`, every crew member was
`enemy_archetypes.Blue_Faction.Assault_Caucasian`. On these the first passenger becomes
`Red_Faction.Patrol_Driver` and the second `Red_Faction.Assault_Nubian` (22 values):

- `worlds/world1`: `Patrols.Rover`, `Rover.M249_Mounted`, `SwampBoat`, `SwampBoat.M249_Mounted`
- `worlds/world2`: `Patrols.Rover`, `Rover.M2_Mounted`, `Rover.M249_Mounted`, `Rover.MK19_Mounted`,
  `SwampBoat.M249_Mounted`, `SwampBoat.MK19_Mounted`, and `JeepLiberty`, whose second seat was empty
  in vanilla and is now crewed.

Guard posts, convoys and the remaining patrols stay `Blue_Faction` (`patrols-blue-crews`). A crew of
the other faction attacks the blue soldiers it passes; this is the "Faction (Enemy infighting)"
recipe of Boggalog's guide
([patrols](../../../docs/docs/modding/guide/patrols.md#faction-enemy-infighting)).

## Depends on

`patrols-drivers` for `Red_Faction.Patrol_Driver`, a new archetype; `Red_Faction.Assault_Nubian` is
vanilla. The vehicles some of these patrols now drive are `patrols-vehicle-mix`.
`ai-assassination-target-player-only` keeps the roaming red crews from killing assassination
targets.

## Compared with Scubrah's Patch

[`faction-conflicts`](../../scubrahs-patch/features/faction-conflicts.md) adds nine new red-crewed
patrol archetypes that a script rolls in about half the time, so any road can meet either faction.
Here a patrol type's faction is fixed: every Land Rover patrol, the Unimog patrol and all swamp-boat
patrols but world 2's M2 boat are red; the Datsun, Wrangler and buggy, fishing-boat and convoy
patrols stay blue.

## Uncertain

- Which faction rules which part of the map (`MapArmy`) is unchanged; a red patrol driving through
  a red-held area meets no blue camps there (inference).
