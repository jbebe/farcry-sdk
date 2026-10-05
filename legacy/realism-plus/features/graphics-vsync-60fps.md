---
title: VSync on, frame rate limited to 60
kind: component
bundle: graphics
status: located
systems: [graphics, engine]
match:
  - "engine/settings/defaultrenderconfig.xml#@{VSync,MaxFps}"
exclude: []
requires: []
existing: mods/UFCP — Maximum frame rate option (gfx_MaxFps)
verified: diff
---

# VSync on, frame rate limited to 60

The game starts with vertical sync on and its own frame limiter at 60 frames per second.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile` attributes:

- `VSync` 0 -> 1;
- `MaxFps` 9999 -> 60, the value behind `gfx_MaxFps`.

These are the defaults a new profile starts from; the VSync option in the display menu still changes
the first one.

## Uncertain

- The mod's notes describe this as an "unlocked frame rate with vsync on". The file caps the limiter
  at 60, so above-60 Hz displays would stay at 60 unless the `Dunia.dll` or a user setting lifts it;
  that is not checked here.
