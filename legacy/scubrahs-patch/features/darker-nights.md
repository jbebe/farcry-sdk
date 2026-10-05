---
title: Darker nights
kind: component
bundle: visuals
claims:
  - "Nights are darker and more atmospheric"
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#**/curveAmbientLightHDRMul/**"
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[+11]"
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{723,1908}"
exclude: []
requires: [graphics-environment-manager]
existing: mods/sky-overhaul — layer night lighting presets and src/night.cpp
verified: diff
---

# Darker nights

From 22:00 to 06:30 the world is lit with the darker lighting preset the base game keeps for
scripted scenes, instead of the weather's own. With the `DarkerNights` setting on, the night also
drops the colour grade and bloom.

## How

`graphics-environment-manager` sets the lighting override `Default.ScriptedEvent.Lighting` at night.
That preset's night ambient colour is far darker than the weather presets': `gradAmbientLightColor`
`0x614029` at midnight against `Default.Clear.Lighting`'s `0xFF6441`, whose ambient multiplier is
also 2.0 at night.

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`:

- `Default.ScriptedEvent.Lighting`'s `curveAmbientLightHDRMul` is replaced: 8 knots running 0.35
  at midnight to 0.75 at noon -> 24 knots, all 0.9466. Raising the night value keeps the preset
  dark rather than black (inferred).
- **`Default.Disabled.AdaptiveBloom`** (new, `[+11]`): `bEnabled` False, neutral remap, contrast 0,
  saturation 1. The manager adds it as an adaptive-bloom override at night when `DarkerNights` is 1
  and `VanillaColorgrading` is 0.

`domino/user/a1sm01_townescape.a1sm01_mission.lua`, base lines 723 and 1908: the town escape's
lighting override named `Default.ScriptedEvent.`, which no preset is called, now names
`Default.ScriptedEvent.Lighting`. That is the same preset, so the escape matches the mod's nights.

## Depends on

- `graphics-environment-manager`, which applies all of this.
- The `DarkerNights` and `NightEnvironmentEnabled` globals and the setting in `ScubrahsPatch.lua`,
  claimed elsewhere. The on-load script `_hash/13e95c15.bin` re-applies the override after a
  reload.

## Uncertain

- That the two mission hunks were made for the nights, rather than as a separate fix of the
  mission's preset name, is an inference.
- What the base game's `Default.ScriptedEvent.` override resolved to is not traced.
