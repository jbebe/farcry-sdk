---
title: VSync on, frame limiter at 999
kind: component
bundle: graphics
claims:
  - "Removed internal frame rate cap, reverted default machete, and hopefully fixed the random boot instability for the steam version."
status: partial
systems: [graphics, engine]
match:
  - "engine/settings/defaultrenderconfig.xml#@{VSync,MaxFps}"
exclude: []
requires: []
existing: mods/UFCP — Maximum frame rate option (gfx_MaxFps)
verified: diff
---

# VSync on, frame limiter at 999

A new profile starts with vertical sync on and the engine's own frame limiter at 999 frames per
second, which in practice is no cap. This is the frame-rate part of the readme's 2-15-21 line.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile` attributes:

- `MaxFps` 9999 -> 999, the value behind `gfx_MaxFps`;
- `VSync` 0 -> 1.

Against the Steam game the limiter change is no change in practice: both values are far above any
display. The line describes Redux's own history: an earlier release set a low cap, and 3.3 lifts it
again. The readme instead tells players to cap the frame rate with the bundled Multi-Fixer
(`engine-multi-fixer`) "to prevent bugs"; VSync on caps it at the display's refresh rate meanwhile.

## The rest of the line

- "reverted default machete" is a weapons change, not here.
- "hopefully fixed the random boot instability for the steam version": no change in the UI,
  settings or graphics containers is identifiable as a boot fix. The Steam-specific build of the
  patch (separate Steam and GOG versions since 2-17-21) may be what is meant.

## Uncertain

- The earlier cap's value is not in this archive.
