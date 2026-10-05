---
title: Enemy infighting - six patrol types crewed by the other faction
kind: component
bundle: gameplay
status: located
systems: [patrols, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/ghostpatrols/patrols/{jeepwrangler,rover/m249_mounted,rover/m2_mounted,swampboat,swampboat/mk19_mounted,fishingboat/m249_mounted}.xml#Entity/Ghost/Passengers/**"
exclude: []
requires: []
verified: diff
---

# Enemy infighting - six patrol types crewed by the other faction

Six patrol types are crewed by `Red_Faction` soldiers, so they trade fire with the guard posts,
convoys and patrols of the other faction they meet. This is the "NPC clashes" of the mod's
description; no readme line names it.

## How

The mod copies the patrol archetypes into `generated/entitylibrarypatchoverride.fcb/ghostpatrols/`
(each redeclaring the `worlds/world1` or `worlds/world2` one; the copies of patrols both worlds
share are identical in both, so they apply in both). Every vanilla crew member was
`enemy_archetypes.Blue_Faction.Assault_Caucasian`. 12 values:

| Patrol (`GhostPatrols.Patrols.…`) | World | Crew in the mod |
|---|---|---|
| `JeepWrangler` | both | `Red_Faction.Assault_Caucasian`, plus a new second seat `Red_Faction.Assault_Nubian` |
| `Rover.M249_Mounted` | both | `Red_Faction.Assault_Nubian` x2 |
| `Rover.M2_Mounted` | 2 | `Red_Faction.Assault_Caucasian` x2 |
| `SwampBoat` | 1 | `Red_Faction.Assault_Caucasian` x2 |
| `SwampBoat.MK19_Mounted` | 2 | `Red_Faction.Assault_Caucasian`, `Red_Faction.ShotgunMan_Caucasian` |
| `FishingBoat.M249_Mounted` | 2 | `Red_Faction.Assault_Caucasian`, `Red_Faction.Assault_Nubian` |

All of these red archetypes are vanilla, in both world libraries. A crew of the other faction
attacks the blue soldiers it passes; this is the "Faction (Enemy infighting)" recipe of Boggalog's
guide ([patrols](../../../docs/docs/modding/guide/patrols.md#faction-enemy-infighting)).

## Depends on

Nothing. The Wrangler patrol now drives a Unimog (`patrols-vehicle-mix`), which the added second
seat fills.

## Compared with other mods

Realism Plus crews the Land Rover, Unimog and swamp-boat patrols red with its own driver archetype
([`patrols-red-faction-crews`](../../realism-plus/features/patrols-red-faction-crews.md));
Scubrah's Patch rolls a red crew about half the time on any patrol
([`faction-conflicts`](../../scubrahs-patch/features/faction-conflicts.md)). Here a patrol type's
faction is fixed.

## Uncertain

- Whether a red patrol driving through an area its own faction holds meets anything to fight there
  is not checked (`MapArmy` is unchanged).
