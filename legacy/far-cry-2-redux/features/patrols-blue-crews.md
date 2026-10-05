---
title: Mixed crews and an extra seat on the remaining patrols
kind: component
bundle: gameplay
status: located
systems: [patrols, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/ghostpatrols/{convoy/escortvehicle,missionspecific/copkiller,patrols/datsun,patrols/jeepliberty,patrols/rover/mk19_mounted,patrols/swampboat/m2_mounted}.xml#Entity/Ghost/Passengers/**"
exclude: []
requires: []
verified: diff
---

# Mixed crews and an extra seat on the remaining patrols

Patrols that stay with the vanilla faction no longer carry only white riflemen: black riflemen and
shotgunners join them, and two patrols gain a seat.

## How

`generated/entitylibrarypatchoverride.fcb/ghostpatrols/`, copies of the world-library archetypes
(applying in both worlds where the patrol exists in both). Vanilla crews are all
`Blue_Faction.Assault_Caucasian`. 10 values:

| Archetype (`GhostPatrols.…`) | World | Change |
|---|---|---|
| `Convoy.EscortVehicle` | both | driver -> `Blue_Faction.Assault_Nubian` (second seat unchanged) |
| `MissionSpecific.CopKiller` | 1 | both escorts -> `Assault_Nubian` (the chief gendarme unchanged) |
| `Patrols.Datsun` | both | rider -> `Assault_Nubian` |
| `Patrols.JeepLiberty` | 2 | driver -> `Assault_Nubian`, plus a new seat `ShotgunMan_Nubian` |
| `Patrols.Rover.MK19_Mounted` | 2 | both -> `Assault_Nubian`, plus a new third seat `Assault_Nubian` |
| `Patrols.SwampBoat.M2_Mounted` | 2 | second seat -> `ShotgunMan_Nubian` |

`Patrols.Rover`, `Patrols.SwampBoat.M249_Mounted`, `Convoy.ConvoyTarget` and
`Convoy.AssassinationTarget` are copied unchanged. These are the "Enemy type" and "Enemy ethnicity"
patrol recipes of Boggalog's guide
([patrols](../../../docs/docs/modding/guide/patrols.md#enemy-ethnicity)).

## Depends on

Nothing. The Datsun and Liberty patrols drive the DLC quad and Unimog now (`patrols-vehicle-mix`);
the Liberty's new seat goes with the Unimog.

## Compared with Realism Plus

[`patrols-blue-crews`](../../realism-plus/features/patrols-blue-crews.md) recrews the same kind of
seats with a new driver archetype; this mod uses vanilla riflemen and shotgunners only.
