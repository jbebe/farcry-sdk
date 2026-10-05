---
title: Texture filtering
kind: component
bundle: visuals
claims:
  - "Improved native texture filtering settings (thanks Parallellines)"
status: located
systems: [graphics]
match:
  - "engine/shaders/samplerstates.xml#**"
exclude: []
requires: []
verified: diff
---

# Texture filtering

World, character, weapon, vehicle, foliage and terrain textures are sampled with 16x anisotropic
filtering at every quality setting.

## How

`engine/shaders/samplerstates.xml` is rewritten; all 37 named sampler states remain.

- **In the base game** 15 states (`AAAColorWrap`, `AAANormalWrap`, `SkinClothColorWrap`,
  `SkinClothNormalWrap`, `WeaponColorWrap`, `VehicleColorWrap`, `VehicleNormalWrap`, `LeafWrap`,
  `LeafClamp`, `BigLeafColorWrap`, `TerrainColorWrap`, `TerrainNormalWrap`, `RoadColorWrap`,
  `RoadNormalWrap`, `Resolve`) have a `base` element plus `ultrahigh`/`high`/`medium`/`low`/`legacy`
  overrides. Anisotropic minification is on only at the upper levels, from 2x (`SkinCloth*`,
  `Vehicle*` at `ultrahigh` only) to 16x (`Leaf*`, `TerrainNormalWrap` at `ultrahigh`), and
  magnification stays linear. The rest are a single `medium` element.
- **In the mod** every state is a single `quality level="medium"` element. Those 15 get `minfilter`
  and `magfilter` `anisotropic`, `maxanisotropy` 16. So do `ColorWrap`, `ColorClamp`, `ColorMirror`,
  `NormalWrap`, `NormalClamp`, `TerrainColorClamp` (linear before), and `WaterNormal` (4x, 8x at
  `ultrahigh`) and `ColorWrapAniso` (anisotropic minification only). Several carry the comment
  `added missing aniso`.
- Depth, shadow, point and 2D samplers keep their filters.

With the per-quality overrides gone, the in-game texture quality setting no longer changes
filtering.

## Uncertain

- That a lone `medium` level applies at every setting is inferred from the layout; how the engine
  picks a level when the requested one is missing is not traced.
