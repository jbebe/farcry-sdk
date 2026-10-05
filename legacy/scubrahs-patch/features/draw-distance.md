---
title: Longer first-person draw distance
kind: component
bundle: improved-graphics
status: located
systems: [graphics, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/cameras/**#**/fFarDistance"
  - "worlds/*/generated/world*.game.xml/_environment.xml#Camera@ViewDistance"
exclude: []
requires: []
verified: diff
---

# Longer first-person draw distance

The player camera's far clipping plane moves out ten times, so distant terrain and objects are drawn
instead of cut off. This is the "increases draw distance" of the Improved Graphics headline.

## How

The mod adds a copy of `cameras.Camera.First` to `generated/entitylibrarypatchoverride.fcb`, which
outranks the world libraries' declaration. Compared with that declaration, it changes two values:
`CCameraPawnComponent/fFarDistance` `1000` -> `10000` (this page) and `fFOV` `75` -> `95`
(`default-fov-95`). Picked alone, this page's copy keeps the base game's FOV.

## Depends on

- How far anything is actually drawn is also bounded by the streaming and LOD settings
  (`streaming-radius`, `lod-distances`).