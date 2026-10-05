---
title: Faster swimming and diving
kind: component
bundle: balancing
claims:
  - "Increased swimming speed"
status: located
systems: [player]
match:
  - "**/entitylibrary*.fcb/player/**#**/f{Swimming,Diving}{MaxSpeed,Acceleration}"
exclude: []
requires: []
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

The mod adds these copies to the override library, which outranks the world libraries. Each copy is compared with the declaration it overrides, so its values are separate changes and this page can be picked alone; the copy it writes keeps the base game's other values. The same copies carry `jump-height`.
