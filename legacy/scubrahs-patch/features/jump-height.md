---
title: Higher jump
kind: component
bundle: balancing
claims:
  - "Slightly increased player jump height"
status: located
systems: [player]
match: []
exclude: []
requires: [player-pawnplayer-copies]
verified: diff
---

# Higher jump

The player jumps a tenth higher.

## How

`CPawn/Body/fJumpHeight` `1` -> `1.1` in the mod's copies of `player.MainCharacter.PawnPlayer` and
its twelve per-character children in `generated/entitylibrarypatchoverride.fcb`.

## Depends on

Those copies are whole units, shared with `swim-speed`: `player-pawnplayer-copies`. This page has
no change of its own.
