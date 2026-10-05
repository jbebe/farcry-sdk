---
title: No rim lighting on characters
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#**/curveRimLightIntensity/**"
exclude: []
requires: []
existing: mods/sky-overhaul — layer (managers.fcb lighting presets, no rim light at night)
verified: diff
---

# No rim lighting on characters

The rim light that outlines characters, strongest at night, is gone in every weather and at every
hour. No readme line names it.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`: every lighting preset's
`curveRimLightIntensity` is emptied, 203 values per world. Each curve's `hidNumKnots` becomes `0`
and every knot's `Value` and `Info` are zeroed, though the knots stay in the file. Eight of the
nine curves change (`Template[4]`, `[6]`, `[25]`, `[31]`, `[38]`, `[40]`, `[41]`, `[44]`, each
its `Template[1]` child); the ninth has no knots in the base game. The base-game values by preset
are listed on Scubrah's Patch's page.

This is the same result as Scubrah's Patch's
[`no-night-rim-light`](../../scubrahs-patch/features/no-night-rim-light.md), which drops the knots
instead of zeroing them.

## Uncertain

- That an empty curve evaluates to zero, rather than to a default, is inferred.
