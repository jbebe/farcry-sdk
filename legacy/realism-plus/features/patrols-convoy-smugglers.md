---
title: Convoys crewed by smugglers
kind: component
bundle: gameplay
status: located
systems: [patrols, ai, missions]
match:
  - "worlds/*/generated/entitylibrary.fcb/ghostpatrols/convoy/{convoytarget,escortvehicle}.xml#Entity/Ghost/Passengers/Passenger[*]/archPassenger"
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/blue_faction/smuggler_{driver,gunner}.xml"
  # outside this page's containers, claimed precisely: the kit model
  - "_hash/2899c2ea.xbg"
exclude: []
requires: [weapons-new-enemy-packs]
verified: diff
---

# Convoys crewed by smugglers

The trucks of convoy missions and their escorts are crewed by smugglers in their own outfit: drivers
with combat shotguns and pistols, escort gunners with rifles and machine guns.

## How

- **Seats.** `GhostPatrols.Convoy.ConvoyTarget` (the convoy's big truck) and `Convoy.EscortVehicle`
  in `worlds/world1` and `worlds/world2` `entitylibrary.fcb/ghostpatrols/convoy/`: their
  `Blue_Faction.Assault_Caucasian` passengers become `Blue_Faction.Smuggler_Driver` (both drivers)
  and `Smuggler_Gunner` (the escort's second seat), 6 values.
- **Archetypes.** `enemy_archetypes.Blue_Faction.Smuggler_Driver` and `Smuggler_Gunner`, new whole
  units in `generated/entitylibrarypatchoverride.fcb/enemy_archetypes/blue_faction/`. Each copies
  `Blue_Faction.Assault_Caucasian` with: kit `merc_kit_smuggler.xml` loading
  `graphics\Characters\Mercenaries\Merc_Kit_Smuggler.xbg`; tags `nubian` and `smuggler`, with
  `<criteria tag="smuggler" criteria="exclude" />` on 11 parts of the kit and kit randomising off
  (`bRadomize` `False`); its own `PartOverwrite` list; pack `smugglerdriver` or `smugglergunner`;
  `fPreCombatMultiplier` `0.6`; no `FootstepSpeedSwitch` entries.
- **Kit model.** `_hash/2899c2ea.xbg` is `graphics\characters\mercenaries\merc_kit_smuggler.xbg` by
  its name hash: a copy of `merc_kit.xbg` with 11 part mesh names broken (`P_MC_LB_Glass2` ->
  `P_MC_LB.Glass2`: glasses, no-gear, boots, military and sport trousers, jeans, shorts, leather
  vest), as many as the parts the criteria exclude, presumably the same ones. A kit part whose mesh
  name the model no longer has cannot be drawn (inference).

## Depends on

- `weapons-new-enemy-packs` for the packs the two archetypes name: `smugglerdriver` (SPAS-12 or
  USAS-12, a Star .45, Desert Eagle or Uzi) and `smugglergunner` (AK-47, FN FAL, M16, M249 or PKM, a
  Desert Eagle, Uzi or DLC sawed-off), both with the merc flare gun and three M67 grenades.
- The escort vehicle itself changes on `patrols-vehicle-mix` (an M2 Land Rover in world 1, the
  Unimog in world 2), which gives the smuggler gunner a mounted gun in world 1.

## Uncertain

- As with `patrols-drivers`, whether the engine resolves the new pack names is not traced.
- Which parts the broken names and criteria leave the smugglers wearing is not checked in game.
