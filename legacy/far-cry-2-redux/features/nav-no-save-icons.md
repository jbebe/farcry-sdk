---
title: No save-point icons on the map and GPS
kind: component
bundle: navigation
claims: []
status: located
systems: [ui]
match:
  - "graphics/objects/mapcompass/save_icon.xbt"
  - "graphics/objects/mapcompass/icon_savegps.xbg"
exclude: []
requires: []
verified: diff
---

# No save-point icons on the map and GPS

Save points (safehouse beds, bus stops) are no longer marked with the floppy-disk icon on the paper
map or the GPS.

## How

- `graphics/objects/mapcompass/save_icon.xbt`, 64x64, DXT5 -> DXT1 with every pixel transparent.
  It is the only texture of the `NEWSAVE` material
  (`graphics\_materials\madeslongchamps-m-2908200831357162.xbm`), which the three save icon models
  `icon_save.xbg`, `icon_savegps.xbg` and `icon_savegpsveh.xbg` use, so all three draw nothing.
- `graphics/objects/mapcompass/icon_savegps.xbg` is also replaced by the broken model of
  `nav-no-gps-safehouses`: the base `icon_savegpsveh.xbg` with every zero byte (348 of 912) turned
  into a space (`0x20`), which no longer parses as a model. Redundant with the blank texture.

`legacy/realism-plus` `nav-no-save-icons` hides the same icons by blanking their archetype names in
`Dunia.dll`.

## Uncertain

- What the engine does with the unparseable model (skips it, or logs a load failure) is not traced;
  the mod is widely played, so it does not crash.
