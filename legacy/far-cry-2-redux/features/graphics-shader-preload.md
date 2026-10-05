---
title: More shaders preloaded
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics, engine]
match:
  - "engine/settings/defaultrenderconfig.xml#@MaxShadersToPreload*"
exclude: []
requires: []
verified: diff
---

# More shaders preloaded

The engine compiles more shaders ahead of use, and more of them per frame, so fewer first-time
draws hitch. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile`: `MaxShadersToPreload` 2000 -> 3000,
`MaxShadersToPreloadPerFrame` 200 -> 800.

## Uncertain

- The effect is read from the key names. Whether this is part of the readme's "hopefully fixed the
  random boot instability for the steam version" (`graphics-frame-cap`) is not known.
