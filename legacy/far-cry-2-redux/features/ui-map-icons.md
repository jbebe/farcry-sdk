---
title: Hand-drawn map and HUD icons
kind: component
bundle: navigation
claims: []
status: located
systems: [ui]
match:
  - "ui/textures/hud/icons_objectives/**"
  - "graphics/objects/mapcompass/final_objective_icons{,_mip0}.xbt"
exclude: []
requires: []
verified: diff
---

# Hand-drawn map and HUD icons

The icons on the paper map, the GPS and the HUD are redrawn as rough painted glyphs, mostly without
the disc behind them. Guard posts and safehouses not yet unlocked are no longer marked on the map.
This is the "new hand-drawn map icons" of the mod's description; not a line of the readme.

## How

- `graphics/objects/mapcompass/final_objective_icons.xbt` (64x512) and its full-resolution top
  level `final_objective_icons_mip0.xbt` (128x1024), the atlas the map and GPS icon models draw
  from (material `NEWICONS`), same sizes, DXT5 -> DXT3, whole files:
  - faction and objective glyphs lose their white or coloured discs: the APR star is painted red,
    the UFLL's Africa yellow, the underground's fist becomes a yellow peace-style sign, the health
    cross red, the safehouse tent a green house, the shop items (ammo, explosives, fuel) red;
  - the orange warning triangles become red exclamation marks;
  - the two large objective rings become a painted grey X and O;
  - the locked guard post (soldier with a padlock) and locked safehouse (tent with a padlock) cells
    are empty, so a guard post or safehouse shows on the map only once it is scouted or unlocked;
  - the bus, cocktail, pistol and target icons keep their white discs, the green arrow is
    unchanged and the black arrow is only lightened.
- `ui/textures/hud/icons_objectives/*.xbt`, 30 whole files, 64x64 DXT5 -> DXT1: the same restyle
  for the HUD's objective popups and lists (`icon_objective` a red X, `icon_subvert` a blue O,
  `icon_safehouse` the green house, `icon_safehouse_lock` a question mark, `icon_partner_mission` a
  letter with a red mark). `icon_guardpost` is empty.

The readme's tutorial line about scouting (`TU67A_MESSAGE_1`, "Scouted outposts will be marked on
your map.") is on `strings-tutorial-tweaks`.

## Player position on map variant

The other navigation variants ship the same two atlas files and the same HUD icons; the variants
differ only in `generated/entitylibrarypatchoverride.fcb` (`nav-no-player-position`).

## Uncertain

- That the empty cells are the locked guard post and locked safehouse is read from the base atlas's
  pictures, not traced through the icon models.
- `legacy/realism-plus` `ui-map-icons` redraws the same files in another style; the art is not
  shared.
