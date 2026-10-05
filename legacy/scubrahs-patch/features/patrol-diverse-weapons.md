---
title: Patrol crews carry mixed weapons
kind: component
bundle: gameplay
claims:
  - "Patrol vehicle occupants now carry a more diverse set of weaponry"
status: located
systems: [patrols, ai, weapons]
match:
  - "**/entitylibrary*.fcb/ghostpatrols/**#Entity/Ghost/Passengers/Passenger[*]/archPassenger"
exclude: []
requires: []
verified: diff
---

# Patrol crews carry mixed weapons

Patrol and convoy escort crews are no longer all riflemen: each vehicle mixes shotgunners,
machine gunners, snipers and rocket men.

## How

In the vanilla patrol archetypes of `worlds/world1` and `worlds/world2`
`entitylibrary.fcb/ghostpatrols/`, the existing passengers' `archPassenger`, all
`enemy_archetypes.Blue_Faction.Assault_Caucasian` in the base game, are swapped for other
`Blue_Faction` classes and the `_Nubian` variants (24 values): `RocketMan_*`, `ShotgunMan_*`,
`LightMachineGunner_*`, `Sniper_*`, `CarlGustaf_Nubian`, `Assault_Nubian`. This covers
`Patrols.Datsun`, `JeepWrangler`, `JeepLiberty`, `Rover`, `Rover.{M249,M2,MK19}_Mounted`,
`SwampBoat`, `SwampBoat.{M249,M2}_Mounted`, `FishingBoat.M249_Mounted` and `Convoy.EscortVehicle`.
The weapon is the enemy archetype's own loadout, so the class name is the weapon.

The seats added by `patrol-every-seat` and the new archetypes of `randomized-patrols-core` and
`faction-conflicts` use the same mix. This is the community "Enemy type" patrol recipe
([patrols guide](../../../docs/docs/modding/guide/patrols.md#enemy-type)).
