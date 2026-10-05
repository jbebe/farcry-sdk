---
title: Sawed-off shotgun picture in the spare DLC icon slot
kind: component
bundle: weapons
status: located
systems: [ui, weapons]
match:
  - "ui/textures/hud/icons_weapons/hud_icon_dlc_06.xbt"
exclude: []
requires: []
verified: diff
---

# Sawed-off shotgun picture in the spare DLC icon slot

The unused sixth DLC weapon icon, a "PLACEHOLDER 6" label in the base game, becomes a coloured
picture of the sawed-off shotgun.

## How

`ui/textures/hud/icons_weapons/hud_icon_dlc_06.xbt`, 128x32 -> 512x128 DXT5, replaced whole.

## Depends on

- `weapons-sawedoff-hud-icon`: the mod's `Dunia.dll` maps the sawed-off shotgun to this slot. Without
  that patch nothing shows this texture; without this texture the patched sawed-off shows the
  placeholder.
