---
title: Alpha to coverage on
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#@AlphaToCoverage"
exclude: []
requires: []
verified: diff
---

# Alpha to coverage on

Cut-out textures (leaves, grass, fences) are antialiased along their edges when multisampling is
on. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile`: `AlphaToCoverage` 0 -> 1.

## Uncertain

- Alpha to coverage only works with multisample antialiasing; the default `MultiSampleMode` stays
  0, so it does nothing until the player turns antialiasing on. Not checked in game.
