---
title: Mixed crews on the remaining patrols
kind: component
bundle: gameplay
status: located
systems: [patrols, ai]
match:
  - "worlds/*/generated/entitylibrary.fcb/ghostpatrols/**#Entity/Ghost/Passengers/Passenger[*]/archPassenger"
exclude:
  # the red crews and the convoy smugglers have their own pages
  - "worlds/world1/generated/entitylibrary.fcb/ghostpatrols/patrols/{rover,rover/m249_mounted,swampboat,swampboat/m249_mounted}.xml#**"
  - "worlds/world2/generated/entitylibrary.fcb/ghostpatrols/patrols/{jeepliberty,rover,rover/m249_mounted,rover/m2_mounted,rover/mk19_mounted,swampboat/m249_mounted,swampboat/mk19_mounted}.xml#**"
  - "worlds/*/generated/entitylibrary.fcb/ghostpatrols/convoy/{convoytarget,escortvehicle}.xml#**"
requires: [patrols-drivers]
verified: diff
---

# Mixed crews on the remaining patrols

The patrols that stay with the vanilla faction no longer carry only white riflemen: the driver has a
shotgun and sidearm, and the gunner is a black rifleman.

## How

In `worlds/world1` and `worlds/world2` `entitylibrary.fcb/ghostpatrols/`, vanilla
`Blue_Faction.Assault_Caucasian` passengers become (14 values):

| Archetype | World | Driver | Second seat |
|---|---|---|---|
| `Convoy.AssassinationTarget` | both | `Blue_Faction.Patrol_Driver` | `Blue_Faction.Assault_Nubian` |
| `MissionSpecific.CopKiller` | 1 | `Patrol_Driver` | `Assault_Nubian` |
| `Patrols.FishingBoat.M249_Mounted` | 2 | `Patrol_Driver` | `Assault_Nubian` |
| `Patrols.SwampBoat.M2_Mounted` | 2 | `Patrol_Driver` | `Assault_Nubian` |
| `Patrols.JeepWrangler` | both | `Patrol_Driver` | - |
| `Patrols.Datsun` | both | `Assault_Nubian` | - |

The third seat of `Convoy.AssassinationTarget` (the target) and of `CopKiller` (the chief gendarme)
is unchanged. This is the "Enemy type" and "Enemy ethnicity" patrol recipes of Boggalog's guide
([patrols](../../../docs/docs/modding/guide/patrols.md#enemy-ethnicity)).

## Depends on

`patrols-drivers` for `Blue_Faction.Patrol_Driver`. The Wrangler and Datsun patrols drive other
vehicles now (`patrols-vehicle-mix`).

## Compared with Scubrah's Patch

[`patrol-diverse-weapons`](../../scubrahs-patch/features/patrol-diverse-weapons.md) gives the same
seats vanilla's other soldier classes (shotgunners, machine gunners, rocket men); this mod gives the
driver a dedicated close-range archetype instead.
