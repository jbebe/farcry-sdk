---
title: Faction Conflicts
kind: component
bundle: features
claims:
  - "Faction Conflicts: Encounter random enemy firefights between the APR & UFLL"
status: located
systems: [patrols, ai]
match:
  - "**/entitylibrary*.fcb/ghostpatrols/**_redfaction.xml"
exclude: []
requires: [randomized-patrols-core]
verified: diff
---

# Faction Conflicts

About half of the randomized patrols roll a crew of the other faction, which fights the vanilla
patrols and outposts it drives past instead of ignoring them.

## How

Nine new patrol archetypes in each of `worlds/world1` and `worlds/world2`
`entitylibrary.fcb/ghostpatrols/patrols/` (18 new units): `Datsun_RedFaction`,
`JeepLiberty_RedFaction`, `JeepWrangler_RedFaction`, `Rover_RedFaction`, `Buggy_RedFaction`,
`Quad_RedFaction` and `Rover/{M2,M249,MK19}_Mounted_RedFaction`. Each copies its blue archetype with
every `archPassenger` drawn from `enemy_archetypes.Red_Faction.*` instead of `Blue_Faction.*`.
The randomizer of `randomized-patrols-core` spawns them in the `RedPatrol1`-`9` slots, 9 of its 17
rolls. Vanilla patrols, convoys and outposts are crewed by `Blue_Faction` archetypes, so a red crew
meeting them starts a firefight.

That a patrol crewed by the other faction attacks the camps it passes is the community-documented
patrol recipe ([patrols guide](../../../docs/docs/modding/guide/patrols.md#faction-enemy-infighting),
[data recipes](../../../docs/docs/modding/data-recipes.md)); the mod adds nothing else for it.

## Depends on

`randomized-patrols-core` owns the missions, layout entries, entities and scripts that put these
archetypes on the road; the red-slot entities there name these archetypes, so the two pages only
work together.

## Uncertain

- That Blue and Red faction archetypes stand for APR and UFLL, as the claim says, is not checked.
