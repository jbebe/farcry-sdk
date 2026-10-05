---
title: Frame rate capped at 150
kind: component
bundle: fixes
claims:
  - "Capped framerate to 150 to prevent player physics issues (mainly jump height)"
status: located
systems: [engine, player]
match:
  - "engine/settings/defaultrenderconfig.xml#@MaxFps"
exclude: []
requires: []
existing: mods/UFCP — Maximum frame rate option (gfx_MaxFps)
verified: diff
---

# Frame rate capped at 150

The game's frame rate is limited to 150, so player physics that misbehave at very high frame rates,
mainly jump height, stay in range.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile` attribute `MaxFps` `9999` -> `150`. This is
the engine's own limiter, the value behind `gfx_MaxFps`.
