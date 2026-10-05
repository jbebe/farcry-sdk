---
title: Breaking a ceasefire starts a three-way fight
kind: component
bundle: gameplay
claims:
  - "Breaking a ceasefire will now result in a gunfight between both factions as well as the player (thanks Lasercar)"
status: located
systems: [ai, world]
match:
  - "domino/user/common_hq_doorman.hqdoorman_init_interact_combat.lua@L{862,941}"
exclude: []
requires: []
verified: diff
---

# Breaking a ceasefire starts a three-way fight

When the player breaks the ceasefire in a town, the two factions' guards turn on each other as well
as on the player, as they do during the opening escape from Pala.

## How

`domino/user/common_hq_doorman.hqdoorman_init_interact_combat.lua`, the script of the faction HQ
doormen:

- `@L941`: in `f_63_Out_2`, where the script forces the town's social region to combat
  (`ForceSocialRegionToCombat`, the ceasefire breaking), it now also calls the engine function
  `StartTownEscape()` - the one the opening mission uses to pit the factions against each other in
  town - but only while `Globals.MASTER_GameGlobals.FinalStoryMissionCompleted` is `0` (comment:
  "Pit factions against each other when the player breaks the cease-fire - Update 3.4: Disable this
  behavior after the last mission").
- `@L862`: in `f_55_Removed`, when the doorman is unloaded with his sector, it calls
  `StopTownEscape()` (comment: "Update 3.2"), so the fight does not outlive the player's visit.

## Depends on

- `FinalStoryMissionCompleted` is declared in `domino/user/master_gameglobals.globals.lua@L90` and set
  to `1` in `domino/system/popupendofgame.lua`; both are on other pages. Without them the global is
  nil, the `== 0` test fails, and the fight never starts, so the declaration at least must be picked.
- The machete-takedown fix's `TownKillListener` scripts (`_hash/19509b46.lua`, `_hash/5ef0e196.lua`)
  call `StartTownEscape()` the same way when a town merc is killed; they are not part of this page.
