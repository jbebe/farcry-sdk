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

The rim light that outlines characters, brightest at night, is gone in every weather and at every
hour.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`: the `curveRimLightIntensity` curve of every
`CEnvironmentLighting` preset is emptied - `hidNumKnots` -> `0` and every knot's values zeroed (396
values). The presets: `Default.HoD`, `Default`, `Storm`, `Cloudy`, `Clear`, `ScriptedEvent`, `Jungle`
`.Lighting` and the preset named `Default.` (vanilla curves of 1 to 12 knots, up to 0.28 at night in
the clear preset).

The result is the same as Scubrah's Patch's
[`no-night-rim-light`](../../scubrahs-patch/features/no-night-rim-light.md), which only sets the
knot counts to 0.

## Uncertain

- That an empty curve evaluates to zero rather than a default is inferred, as there.
