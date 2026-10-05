---
title: Faction map redrawn as a checkerboard
kind: component
bundle: gameplay
status: located
systems: [patrols, world]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/MapArmy/**"
exclude: []
requires: []
verified: diff
---

# Faction map redrawn as a checkerboard

The faction that holds each map sector changes. In the mod, red and blue alternate sector by
sector, a checkerboard across both worlds.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService` → `MapArmy`. One `Item` per sector
(`world`, `name` such as `W1_A_1`, `army`). 26 items change:

- World 1: `W1_A_2`, `W1_A_4`, `W1_B_1`, `W1_B_3`, `W1_C_2` and `W1_D_1` go red to blue. `W1_B_4`,
  `W1_C_3`, `W1_C_5`, `W1_D_4`, `W1_E_1`, `W1_E_3` and `W1_E_5` go blue to red.
- World 2: `W2_A_2`, `W2_A_4`, `W2_B_3`, `W2_B_5`, `W2_C_4` and `W2_D_5` go blue to red. `W2_C_1`,
  `W2_D_2`, `W2_D_4`, `W2_E_1`, `W2_E_3` and `W2_E_5` go red to blue.

## Uncertain

- What reads `MapArmy` is not traced. The likely readers are which faction's patrols and
  reinforcements appear in a sector, and the gameplay manager's sector ownership. An editor map has
  no such sectors, so this only acts in the campaign.
