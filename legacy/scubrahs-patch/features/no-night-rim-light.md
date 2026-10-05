---
title: No player rim lighting
kind: component
bundle: visuals
claims:
  - "Disabled player rim lighting at night"
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#**/curveRimLightIntensity/**"
exclude: []
requires: []
existing: mods/sky-overhaul — layer (managers.fcb lighting presets, no rim light at night)
verified: diff
---

# No player rim lighting

The rim light that outlines characters, strongest at night, is gone in every weather.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`: every `CEnvironmentLighting` preset's
`curveRimLightIntensity` loses all its knots (`hidNumKnots` -> 0). Base-game curves, by time of day:

| Preset | Knots | Values |
|---|---|---|
| `Default.Clear.Lighting` | 12 | 0.28 at night, down to 0.17 at noon |
| `Default.Cloudy.Lighting` | 8 | 0.20 at night, 0.27 at noon |
| `Default.HoD.Lighting`, `Default.Jungle.Lighting` | 8 | 0.10 at night, 0.05 at noon |
| `Default.ScriptedEvent.Lighting` | 2 | 0 |
| `Default.Default.Lighting`, `Default.Storm.Lighting`, the preset named `Default.` | 1 | 0 |

So the rim light is removed at all hours, not only at night; it is brightest at night in the
presets that have one.

## Uncertain

- That an empty curve evaluates to zero, rather than to a default, is inferred from the claim.
