---
title: Clouds, wind and sky occlusion
kind: component
bundle: improved-graphics
claims: []
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[*]/Template[*]/{fAnimationScale,fWindForce}"
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[*]/Template[*]/{FormationLayer1,FormationLayer2,Material}/*"
  - "engine/settings/defaultrenderconfig.xml#@AmbientSkyOcclusion*"
  - "worlds/*/generated/world*.game.xml/_environment.xml#Sky@{SunRange,SunLightHDRMul,MoonHDRMul}"
exclude: []
requires: []
verified: diff
---

# Clouds, wind and sky occlusion

The "improves clouds and lighting" part of Improved Graphics: more cloud cover in clear and desert
weather, faster-animating clouds, stronger wind, and darker sky occlusion under trees and roofs.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`, old -> new.

`CEnvironmentCloud` presets `Default.Clear.Cloud` and `Default.Desert.Cloud`:

| Field | Clear | Desert |
|---|---|---|
| `fAnimationScale` | 1 -> 2 | 1 -> 2 |
| `FormationLayer1/fCoverage` | 0.15 -> 0.45 | 0.18 -> 0.45 |
| `FormationLayer1/fFallOffCurve` | 0.2 -> 0.5 | 0.2 -> 0.5 |
| `FormationLayer1/fNormalStrength` | 10 -> 6 | unchanged |
| `FormationLayer2/fCoverage` | 0.25 -> 0.4 | 0.35 -> 0.375 |
| `FormationLayer2/fFallOffCurve` | 0.1 -> 0.35 | 0.1 -> 0.25 |
| `FormationLayer2/fNormalStrength` | 8 -> 4 | 8 -> 7 |
| `FormationLayer2/fWindSpeedScale` | 5 -> 2 | 5 -> 2 |
| `Material/fDiffuseLightingPower` | 1.5 -> 1.2 | 1.5 -> 1.2 |
| `Material/fSunLightColorSamplingScale` | 0.8 -> 0.85 | 0.8 -> 0.7 |
| `Material/fSubsurfaceScatteringPower` | 700 -> 900 | 700 -> 750 |

`CEnvironmentWind` presets, `fWindForce`: `Default.HoD.Wind` 10 -> 80, `Default.Jungle.Wind`
10 -> 80, `Default.Clear.Wind` 60 -> 80.

`engine/settings/defaultrenderconfig.xml`, root `Profile`:
`AmbientSkyOcclusionMinVisibility` 0.45 -> 0.1, `AmbientSkyOcclusionDynamicMinVisibility`
0.3 -> 0.1, so occluded places can get much darker.

## Uncertain

- The wind force does more than the clouds. In the engine it also bends vegetation, sets the
  physics wind, feeds fire propagation and, above 70, starts wind-blown ambience particles (see
  `docs/docs/engine-internals/time-of-day-and-lighting.md#wind`). Whether the mod raised it for the
  look or for `fire-spread` is not stated.
- The list's headline names no single claim for this page; the grouping is the analysis's.
