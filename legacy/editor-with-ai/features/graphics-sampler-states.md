---
title: 16x anisotropic filtering wherever it is on
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/shaders/samplerstates.xml#**"
exclude: []
requires: []
verified: diff
---

# 16x anisotropic filtering wherever it is on

Every texture sampler that already used anisotropic filtering at a given quality level now uses
16x, so ground, roads, foliage, weapons, vehicles and characters stay sharp at grazing angles at
High and Medium as well as Ultra High.

## How

`engine/shaders/samplerstates.xml`, `maxanisotropy` raised to 16 on 32 quality levels of 16
sampler states. Only the cap changes: no level switches its `minfilter` to `anisotropic`, so levels
that filter linearly stay linear (for example `SkinCloth*` and `Vehicle*` at High and below).

| Sampler state | `ultrahigh` | `high` | `medium` |
|---|---|---|---|
| `AAAColorWrap`, `AAANormalWrap` | 8 -> 16 | 2 -> 16 | — |
| `SkinClothColorWrap`, `SkinClothNormalWrap` | 2 -> 16 | — | — |
| `WeaponColorWrap` | 8 -> 16 | 4 -> 16 | 2 -> 16 |
| `VehicleColorWrap`, `VehicleNormalWrap` | 2 -> 16 | — | — |
| `LeafWrap` | (16) | 8 -> 16 | — |
| `LeafClamp`, `BigLeafColorWrap` | (16) | 8 -> 16 | 2 -> 16 |
| `TerrainColorWrap` | 8 -> 16 | 4 -> 16 | 2 -> 16 |
| `TerrainNormalWrap` | (16) | 8 -> 16 | 2 -> 16 |
| `RoadColorWrap`, `RoadNormalWrap` | 8 -> 16 | 4 -> 16 | 2 -> 16 |
| `Resolve` | 8 -> 16 | 4 -> 16 | 2 -> 16 |
| `WaterNormal` | 8 -> 16 | — | 4 -> 16 |

(16) was already 16; — is a level without anisotropic filtering, left as it is. `WaterNormal` has
only a `medium` and an `ultrahigh` level. `low` and `legacy` are untouched everywhere.

The per-quality structure stays, so the texture quality setting still chooses between filters.
Scubrah's Patch does the same job differently, collapsing every state to one `medium` level with
16x everywhere (see `legacy/scubrahs-patch/features/texture-filtering.md`).

## Uncertain

- 16x costs some GPU time on old hardware; nothing here measures it.
