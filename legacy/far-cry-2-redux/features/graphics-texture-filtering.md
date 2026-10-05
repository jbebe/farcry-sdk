---
title: Sharper texture filtering
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/shaders/samplerstates.xml#**"
exclude: []
requires: []
verified: diff
---

# Sharper texture filtering

Textures are sampled with 16x anisotropic filtering at the upper quality levels and a sharper mip
bias everywhere, so ground, roads, walls and foliage stay crisp at a glancing angle. Not a line of
the readme.

## How

`engine/shaders/samplerstates.xml`, 159 changes; the per-quality structure is kept:

- **The 15 states with quality levels** (`AAAColorWrap`, `AAANormalWrap`, `SkinCloth*`,
  `WeaponColorWrap`, `Vehicle*`, `LeafWrap`, `LeafClamp`, `BigLeafColorWrap`, `Terrain*Wrap`,
  `Road*Wrap`, `Resolve`) and `GenericMaskWrap`: the `base` element's `minfilter` and `mipfilter`
  `linear` -> `anisotropic` and `mipmaplodbias` 0 -> -1; every `maxanisotropy` at `ultrahigh`,
  `high` and `medium` (2, 4 or 8 before) -> 16; the `legacy` level's `point` filters ->
  `anisotropic` and its bias 0.5 -> -1.
- **The single-level states**: `ColorWrap`, `ColorClamp`, `ColorMirror`, `NormalWrap`,
  `NormalClamp`, `ColorBlackTransparentBorder`, `ColorClamp2D`, `ColorPoint*`, `TerrainColorClamp`
  (also bias -1), `SectorHemiMapSampler`, `ColorWrapAniso` and `WaterNormal` (max 16): `minfilter`
  and mostly `mipfilter` -> `anisotropic`.
- **Depth and shadow samplers** (`DepthSampler`, `DepthVPSampler`, `ResolvedDepthVPSampler`,
  `DepthSamplerF4`, `VSMDepthSampler`, `ShadowSampler`, `ShadowRealSampler`): `minfilter` ->
  `anisotropic` as well.

Magnification stays linear. Unlike `legacy/scubrahs-patch` `texture-filtering`, which flattens the
file to one level, the texture quality setting still chooses the anisotropy level here.

## Uncertain

- Direct3D 9 has no anisotropic mip filter; how the engine maps `mipfilter="anisotropic"` (probably
  to linear) is not traced.
- Anisotropic minification on depth and shadow-map samplers, which are read at exact texels, is
  probably ignored or harmless; not checked.
- The -1 mip bias sharpens at the cost of shimmer on fine detail; its look is not checked in game.
