---
title: Machete range and consistent stats
kind: component
bundle: balancing
claims:
  - "Slightly increased the range of all machetes and made their stats consistent"
status: located
systems: [weapons]
match: []
exclude: []
requires: [player-machete-copies]
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
`generated/entitylibrarypatchoverride.fcb`, whole units shared with `machete-creeping`:
`player-machete-copies`. This page has no change of its own.
