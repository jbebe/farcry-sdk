---
title: Full-colour weapon icons on the HUD
kind: component
bundle: ui
status: located
systems: [ui, weapons]
match:
  - "ui/textures/hud/icons_weapons/**"
exclude:
  - "ui/textures/hud/icons_weapons/hud_icon_dlc_06.xbt"
requires: []
verified: diff
---

# Full-colour weapon icons on the HUD

The weapon icons on the HUD, the weapon selection and the arms dealer's screens are no longer white
silhouettes: each is a coloured picture of the weapon, at four times the resolution.

## How

37 whole textures in `ui/textures/hud/icons_weapons/`:

- 35 icons (one per weapon, plus `hud_icon_bullets`, `hud_icon_rpg7_rocket`, `hud_icon_mortar_shop`,
  `hud_silenced_shotgun`, `hud_stricker_crossbow`): mostly 128x32 -> 512x128 DXT5; the square ones
  `hud_icon_mortar` and `hud_icon_rpg7_rocket` 64x64 -> 256x256, `hud_icon_m67` and
  `hud_icon_molotov` 64x64 -> 128x128, `hud_icon_bullets` 32x32 -> 256x256. A white silhouette is
  replaced by a rendered, coloured image of the same item.
- `hud_icon_lpo50.xbt` keeps 128x32 but becomes the coloured flamethrower picture.
- `hud_icon_sawedoff_shotgun.xbt` now holds a coloured flamethrower picture as well, not a shotgun.
  The mod's `Dunia.dll` moves the sawed-off to the `dlc6` icon slot (`weapons-sawedoff-hud-icon`),
  so this slot's picture presumably no longer shows for the sawed-off.

`hud_icon_dlc_06.xbt`, the sawed-off's new icon, is its own page, `ui-hud-icon-dlc06`.

## Uncertain

- Where the old sawed-off slot is still drawn, and so where the flamethrower picture in it shows,
  is not traced.
