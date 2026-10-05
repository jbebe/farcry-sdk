---
title: Longer sun shadows on Ultra High
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Shadow/quality[*]@*"
  - "engine/settings/defaultrenderconfig.xml#Ambient/quality[*]@*"
exclude: []
requires: []
verified: diff
---

# Longer sun shadows on Ultra High

On Ultra High shadows the sharp near shadows reach about 24 m instead of 4 m, the cascades behind
them reach further, leaves cast their full share of shadows, and the static ambient shadowing on
High reaches much further. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`:

- `Shadow/quality[ultrahigh]` (the Ultra High presets): `SunShadowRange0` 4 -> 24,
  `SunShadowRange1` 20 -> 48, `SunShadowRange2` 140 -> 160, `SunShadowFadeRange` 10 -> 14,
  `LeavesShadowRatio` `0.5f` -> 1, `SpotSlopeScaleDepthBias` 20 -> 30. `CascadedShadowMapSize`
  is unchanged, so the same shadow map covers a larger area.
- `Ambient/quality[high]` (every preset from High up): `MaxHemiMapDistance` 160 -> 2000.

The base value `0.5f` is a typo in the base file; whether the engine read it as 0.5 is not known.

## Compared with other mods

`legacy/realism-plus` `graphics-shadows` and `legacy/scubrahs-patch` `shadow-quality` tune the
same keys at every level with shorter ranges and larger maps.

## Uncertain

- Stretching the first cascade from 4 to 24 m without a larger map lowers its resolution about six
  times; how soft the near shadows become is not checked.
- What the spot-light slope bias fixes (acne or peter-panning) is not stated.
