---
title: Faster swimming and diving
kind: component
bundle: balancing
claims:
  - "Increased swimming speed"
status: located
systems: [player]
match: []
exclude: []
requires: [player-pawnplayer-copies]
verified: diff
---

# Faster swimming and diving

The player swims and dives a fifth faster.

## How

`CPawn/Body/fSwimmingMaxSpeed` `5` -> `6`, `fSwimmingAcceleration` `5` -> `6`, `fDivingMaxSpeed`
`5` -> `6` and `fDivingAcceleration` `5` -> `6`, in the mod's copies of
`player.MainCharacter.PawnPlayer` and its twelve per-character children in
`generated/entitylibrarypatchoverride.fcb`.

## Depends on

Those copies are whole units, shared with `jump-height`: `player-pawnplayer-copies`. This page has
no change of its own.
