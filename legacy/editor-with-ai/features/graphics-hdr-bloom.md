---
title: HDR, FP32 HDR and bloom at every PC quality
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#{RenderQuality,CustomQuality}/quality[*]@{Hdr,HdrFP32,Bloom}"
exclude: []
requires: []
verified: diff
---

# HDR, FP32 HDR and bloom at every PC quality

Every DirectX 9 quality preset renders with HDR, its floating-point variant and bloom, the way the
DirectX 10 presets already do. Low and Medium gain HDR (and Low and Legacy bloom), which also cures
the white flashes on vehicles those presets show.

## How

`engine/settings/defaultrenderconfig.xml`, 13 values, all 0 -> 1:

| Preset | `Hdr` | `HdrFP32` | `Bloom` |
|---|---|---|---|
| `RenderQuality/quality[ultrahigh]` | (1) | 0 -> 1 | (1) |
| `RenderQuality/quality[veryhigh]` | (1) | 0 -> 1 | (1) |
| `RenderQuality/quality[high]` | (1) | 0 -> 1 | (1) |
| `RenderQuality/quality[medium]` | 0 -> 1 | 0 -> 1 | (1) |
| `RenderQuality/quality[low]` | 0 -> 1 | 0 -> 1 | 0 -> 1 |
| `RenderQuality/quality[legacy]` | 0 -> 1 | 0 -> 1 | 0 -> 1 |
| `CustomQuality/quality[custom]` | 0 -> 1 | 0 -> 1 | (1) |

(1) was already on. The `*d3d10` presets and `customd3d10` already had all three on and are
untouched; `xenon` and `ps3` keep HDR off.

- Turning on `Hdr` and `Bloom` at Low and Medium is the documented fix for vehicles flashing white
  at low shading settings (`docs/docs/modding/guide/graphics.md#bug-fix---white-flashes-on-vehicles-with-low-settings`).
- `HdrFP32` is the floating-point HDR path the engine registers off by default
  (`docs/docs/engine-internals/sky-and-clouds.md#rendering-an-8-bit-path-by-default-hdr-available-but-unused`).
  On DirectX 9 the base game left it off at every preset; the mod turns it on everywhere.

## Uncertain

- What `HdrFP32` changes on screen under DirectX 9, and whether every DirectX 9 card of the time
  supports it, is not checked. Its cost is a floating-point render target.
- These are the presets' defaults. A player's saved video settings may override them; how the
  game picks between the two is not traced here.
