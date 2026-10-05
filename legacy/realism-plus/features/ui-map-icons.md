---
title: Redrawn map and GPS icons
kind: component
bundle: ui
status: located
systems: [ui]
match:
  - "ui/textures/hud/icons_objectives/**"
  - "graphics/objects/mapcompass/final_objective_icons{,_mip0}.xbt"
exclude: []
requires: []
verified: diff
---

# Redrawn map and GPS icons

The icons for objectives, towns, safehouses, shops, cell towers, outposts and other points of
interest are redrawn in a flatter, hand-drawn style: black glyphs with a white outline, without the
tan disc behind them.

## How

- `ui/textures/hud/icons_objectives/*.xbt`, 30 icons (`icon_bus`, `icon_safehouse`,
  `icon_weaponshop`, `icon_town`, `icon_objective`, `icon_apr`, `icon_ufll`, `icon_celltower` and
  their `_active` / `_lock` forms...), whole files at the same 64x64 DXT5; only the header grows from
  32 to 36 bytes. Some motifs change: the safehouse tent becomes a house with a cross (the symbol of
  the redrawn safehouse signs, `graphics-sign-textures`), the town marker becomes a red dot, the
  objective ring's arrows point inward, the faction stars and Africa outlines sit on flat green and
  yellow discs.
- `graphics/objects/mapcompass/final_objective_icons.xbt` and `final_objective_icons_mip0.xbt`, the
  atlas of icons drawn on the paper map and the GPS (two columns of 16): 64x512 and 128x1024 ->
  512x4096 DXT5, both files the same full-resolution image. The same restyle as the HUD icons,
  eight times the resolution.

## Full Navigation

The Full Navigation variant ships its own `final_objective_icons{,_mip0}.xbt`. The only difference
is the green arrow (row 13, left column): this analysed Limited Navigation copy keeps a vanilla-style
bright green arrow with a black outline (structural similarity 0.97 to the base cell), while Full
Navigation's arrow is redrawn lighter and brush-textured like the other icons (0.88). The HUD icons
are the same in both.

## Uncertain

- Which marker the green arrow is (the player's own position on the GPS, by its colour and shape)
  is not traced; why Limited Navigation keeps it in the original style is not stated.
