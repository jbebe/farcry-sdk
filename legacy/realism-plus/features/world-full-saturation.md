---
title: Full colour saturation in every weather
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[*]/Template[*]/fSaturation"
exclude: []
requires: []
existing: mods/sky-overhaul — src/grade.cpp (replaces the weather presets' grade)
verified: diff
---

# Full colour saturation in every weather

The game's washed-out grade is lifted: colours keep their full saturation.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`, the `CEnvironmentAdaptiveBloom` presets:
`fSaturation` -> `1` on `Default.Jungle`, `Storm`, `Clear` and `Desert.AdaptiveBloom` (from `0.5`),
`ScriptedEvent.AdaptiveBloom` (from `0.52`) and `Default.Default.AdaptiveBloom` (from `0.45`), 12
values. The colour remap, contrast and bloom values are untouched, so the orange tint stays.

## Compared with Scubrah's Patch

[`original-colorgrading`](../../scubrahs-patch/features/original-colorgrading.md) sets the same
`fSaturation` to `1` and also zeroes the per-channel colour remap and contrast, removing the tint.
