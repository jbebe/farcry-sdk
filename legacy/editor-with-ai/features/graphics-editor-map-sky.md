---
title: Bigger, brighter sun and busier clouds on editor maps
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "worlds/tmpla/generated/tmpla.game.xml#Environment/**"
exclude: []
requires: []
verified: diff
---

# Bigger, brighter sun and busier clouds on editor maps

Maps made with the in-game editor get a larger and brighter sun flare, a brighter sun, clouds that
move and animate, and a larger shadow depth bias. The campaign worlds are not affected.

## How

`worlds/tmpla/generated/tmpla.game.xml`, the world descriptor of the template world. When the
editor exports a map it parses this file as the template of the map's own `<name>.game.xml` and
patches only the map name and the default time and storm into it
(`docs/docs/engine-internals/world-loading.md#what-a-world-is-made-of`), so every value below
lands in each map exported after the mod is installed. Old -> new, in the `<Environment>` element:

| Element | Attribute | Base game | Mod |
|---|---|---|---|
| `Sky` | `SunFlareTextureSize` | 0.05 | 0.07 |
| `Sky` | `SunFlareMaxUniformScale` | 3 | 7 |
| `Sky` | `SunFlareMaxBottomScale` | 0.7 | 1.7 |
| `Sky` | `SunFlareHDRMul` | 5 | 10 |
| `Sky` | `SunLightHDRMul` | 2 | 10 |
| `Clouds` | `WindSpeedScaling` | 0.04 | 3.99 |
| `Clouds` | `AnimationScale` | 0 | 1 |
| `Clouds/Formation` | `Coverage` | 0.6 | 7.3 |
| `Clouds/Formation` | `FallOffCurve` | 0.8 | 8.4 |
| `Clouds/Formation` | `NormalStrength` | 15 | 30 |
| `Clouds/Formation` | `Tolerance` | — | `0.0` (new attribute) |
| `Clouds/Material` | `SpecularLightingPower` | 3 | 10 |
| `Clouds/Material` | `SubsurfaceScatteringPower` | 500 | 1000 |
| `Clouds/Material` | `SubsurfaceScatteringBias` | 0.2 | 1.0 |
| `Shadow` | `SunShadowZOffset` | 0 | 6 |
| `Shadow` | `SunShadowZOffsetSlopeScale` | 3 | 11 |

The base values are the same ones the campaign worlds carry
(`docs/docs/modding/environment-presets.md#the-descriptor-names-the-presets`). The flare's size on
screen is `SunFlareTextureSize` (`docs/docs/engine-internals/sky-and-clouds.md`), so the flare
starts 40% larger and may grow to more than twice the base game's limits. `SunShadowZOffset` and
its slope scale are a depth bias on sun shadows: more bias removes shadow acne and lets shadows
detach from the objects casting them.

The mod's new sky textures (`graphics-sky-textures`) are a separate change that every world sees.

## Uncertain

- Whether the literal `<Clouds>` block is read at all is not established. The same element names a
  `Cloud` preset by GUID, and the presets in `tmpla.managers.fcb` carry their own coverage and
  lighting. `Coverage` 7.3 and `FallOffCurve` 8.4 are far outside the 0-1 range every shipped
  preset uses, which suggests nobody saw them take effect.
- `Tolerance` is not an attribute any shipped descriptor carries; it is probably ignored.
- What `SunLightHDRMul` scales (the sun sprite or the sun's light) is not traced.
- Maps exported before the mod was installed, and maps downloaded from others, carry their own
  descriptor and keep their values.
