---
title: Medium geometry and ambient under DirectX 10
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#{Geometry,Ambient}/quality[medium]@UnSupportedPlatforms"
exclude: []
requires: []
verified: diff
---

# Medium geometry and ambient under DirectX 10

The Medium level of the geometry and ambient settings stops being marked unsupported on DirectX 10.

## How

`engine/settings/defaultrenderconfig.xml`: `Geometry/quality[medium]` and `Ambient/quality[medium]`
lose their `UnSupportedPlatforms="d3d10"` attribute. `legacy/scubrahs-patch` `dx10-compatibility`
removes the same attribute from more levels.

## Depends on

Nothing; but the mod's own DirectX restart message (`strings-directx-warning`) tells players to use
DirectX 9 anyway.

## Uncertain

- What the engine does with a level marked unsupported (skips it, or substitutes another) is not
  traced.
