---
title: Loadout packs for smugglers and patrol drivers
kind: component
bundle: gameplay
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{smugglerdriver,smugglergunner,patroldriver}]"
exclude: []
requires: []
verified: diff
---

# Loadout packs for smugglers and patrol drivers

Three new inventory packs arm the mod's new smuggler and patrol-driver enemies.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks` gains (each pack a
whole new element; probabilities summed over the 28 levels):

- `Pack[smugglerdriver]`: SPAS-12 or USAS-12, half and half; sidearm Desert Eagle most likely
  (`0.50` at level 0), then Star .45 and Uzi; the flare gun and three frag grenades.
- `Pack[smugglergunner]`: AK-47, FAL, M16, M249 or PKM (the rifles most likely); sidearm Desert
  Eagle, sawed-off or Uzi at levels 0-14 only; flare gun, three frags.
- `Pack[patroldriver]`: SPAS-12 most likely, then Ithaca, USAS-12 and rarely the silenced shotgun;
  any of the six pistols and SMGs as sidearm; flare gun, three frags.

The packs are named by the new archetypes `Blue_Faction.Smuggler_Driver`, `Blue_Faction.Smuggler_Gunner`
and `Blue_Faction`/`Red_Faction.Patrol_Driver` in `generated/entitylibrarypatchoverride.fcb`
(`enemy_archetypes`, the AI pages); without them the packs do nothing.

## Depends on

- The same block adds three `specops*` packs; those belong with the special-forces archetypes
  (`ai-specops-a1lm04`). `Pack[specopssniper]` is named by no archetype, but the placed sniper the
  mod converts in `w1_d_4` names it on its instance `CPawn`.
