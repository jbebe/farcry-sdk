---
title: Machete range and consistent stats
kind: component
bundle: balancing
claims:
  - "Slightly increased the range of all machetes and made their stats consistent"
status: located
systems: [weapons]
match:
  - "**/entitylibrary*.fcb/weaponproperties/handtohand/**#**/{fMaxAttackDistance,fAttackFOV}"
exclude: []
requires: []
verified: diff
---

# Machete range and consistent stats

All four machetes reach further and swing in the same arc.

## How

On all four machete weapon properties (`Machete`, `Machete_HomeMade`, `Machete_Modern`,
`Machete_Primitive`):

- `FireStrategyProperties/fMaxAttackDistance` `3` (Machete, HomeMade) or `5` (Modern, Primitive) ->
  `6`
- `FireStrategyProperties/fAttackFOV` `45` -> `55`

## Depends on

The values sit in the mod's copies of the four archetypes in
`generated/entitylibrarypatchoverride.fcb`. The mod adds these copies to the override library, which outranks the world libraries. Each copy is compared with the declaration it overrides, so its values are separate changes and this page can be picked alone; the copy it writes keeps the base game's other values. The same copies carry `machete-creeping`,
`machete-stealth-kills` (`bIsSilent`) and new entity ids (`noise-player-hash-mangling`).
