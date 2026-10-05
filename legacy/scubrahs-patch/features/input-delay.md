---
title: Reduced input delay
kind: component
bundle: fixes
claims:
  - "Reduced input delay"
status: unresolved
systems: [input, engine]
match: []
exclude: []
requires: []
---

# Reduced input delay

Less latency between input and what is seen on screen.

## How

No change found. None of the mod's twenty `Dunia.dll` patches is about input or frame pacing.

Searched: `engine/settings/defaultrenderconfig.xml` (`MaxDriverBufferedFrames` stays 0, `VSync` stays
0), `engine/settings/defaultthreadingconfig.xml` (`RENDER_THREAD` unchanged; the physics and job
thread edits are `graphics-thread-config`), and `config/inputactionmap*.xml` (the only filter edit is
`MouseFilter@maxOutput`, which is `mouse-deceleration`).
