---
title: Higher jump
kind: component
bundle: balancing
claims:
  - "Slightly increased player jump height"
status: located
systems: [player]
match:
  - "**/entitylibrary*.fcb/player/**#**/fJumpHeight"
exclude: []
requires: []
verified: diff
---

# Higher jump

The player jumps a tenth higher.

## How

`CPawn/Body/fJumpHeight` `1` -> `1.1` in the mod's copies of `player.MainCharacter.PawnPlayer` and
its twelve per-character children in `generated/entitylibrarypatchoverride.fcb`.

## Depends on

The mod adds these copies to the override library, which outranks the world libraries. Each copy is compared with the declaration it overrides, so its values are separate changes and this page can be picked alone; the copy it writes keeps the base game's other values. The same copies carry `swim-speed`.
