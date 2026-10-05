---
title: No mouse deceleration
kind: component
bundle: fixes
claims:
  - "Disabled mouse deceleration"
status: located
systems: [input]
match:
  - "config/inputactionmapcommon.xml/*#MouseFilter@maxOutput"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/mouse_speed_cap.cpp
verified: diff
---

# No mouse deceleration

A fast mouse flick turns the view as far as a slow one; the per-frame cap that made quick moves
undershoot is lifted.

## How

`config/inputactionmapcommon.xml`: the `MouseFilter` (`input="mouse:move"`) of six action maps,
`maxOutput` 10 -> 999999: `common_look`, `common_in_vehicle`, `common_using_mounted_weapon`,
`common_scry`, `common_free_camera` and `common_spectator_camera`. `sensitivity` (0.8) and the
`Weapons.AimCurves.Mouse_Curve` curve are unchanged.

UFCP fixes the same cap in code, raising `maxOutput` only for the mouse's filter. Its notes describe
the defect: `CActionMapCurveFilter` applies a gamepad's output ceiling to the mouse.

## Uncertain

- The mod's `Dunia.dll` patches are still being traced; if one of them also touches the mouse
  filter it belongs here.
