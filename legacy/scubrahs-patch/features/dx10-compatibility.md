---
title: DX10 compatibility
kind: component
bundle: features
claims:
  - "DX10 Compatibility: Play with the DX10 API for higher framerates"
status: located
systems: [graphics, engine]
match:
  - "engine/settings/defaultrenderconfig.xml#**@UnSupportedPlatforms"
  - "engine/settings/defaultrenderconfig.xml#RenderQuality/quality[ultrahighd3d10]@*"
exclude: []
requires: []
verified: diff
---

# DX10 compatibility

The DirectX 10 renderer is the base game's own. The mod changes which quality levels it may use, so
that on DX10 the Ultra High preset uses the second-heaviest shadow levels instead of the 4096 maps
that `shadow-quality` gives DX9.

## How

`engine/settings/defaultrenderconfig.xml`. A quality level whose `UnSupportedPlatforms` lists `d3d10`
is not offered under DX10.

- `Geometry/quality[medium]`, `Ambient/quality[medium]` and `Shadow/quality[medium]` lose
  `UnSupportedPlatforms`, `d3d10` -> empty, so DX10 can use them.
- `Ambient/quality[high]` and `Shadow/quality[ultrahigh]` gain `UnSupportedPlatforms`, empty ->
  `d3d10`. These are the two levels `shadow-quality` raises to 4096 maps.
- The `ultrahighd3d10` preset under `RenderQuality`: `AmbientQuality` `high` -> `medium`,
  `ShadowQuality` `ultrahigh` -> `veryhigh`. With `shadow-quality` these mean 2560 maps, not 4096.

## Depends on

Nothing to load, but the point of it is `shadow-quality`: without that page the levels DX10 is
moved to are the base game's weaker ones.

## Uncertain

- That the 4096 maps are what DX10 could not run well is an inference from which levels were
  excluded. The mod's notes give no reason.
- The mod also ships a patched `Dunia.dll`. Whether any of its patches concern DX10 is pending that
  trace.
