---
title: HDR and bloom on the Low and Medium presets
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#RenderQuality/quality[{low,medium}]@{Hdr,Bloom}"
exclude: []
requires: []
verified: diff
---

# HDR and bloom on the Low and Medium presets

Choosing the Low or Medium preset no longer switches HDR lighting off, and Low keeps bloom.

## How

`engine/settings/defaultrenderconfig.xml`, `RenderQuality`:

- `quality[medium]`: `Hdr` 0 -> 1 (`Bloom` was already 1);
- `quality[low]`: `Hdr` 0 -> 1, `Bloom` 0 -> 1.

High and above already had both on. These are the preset defaults; the separate HDR and bloom
options still apply after a preset is chosen.

## Uncertain

- Not in the mod's notes; the purpose (keeping the look consistent across presets) is inferred.
