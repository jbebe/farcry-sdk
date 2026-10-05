---
title: New camouflage prints on character clothing
kind: component
bundle: gameplay
claims: []
status: located
systems: [graphics, player, buddies]
match:
  - "graphics/actors/_textures/{c_cm_camo_08_d,camo_pattern_d,print_camo_d}.xbt"
exclude: []
requires: []
verified: diff
---

# New camouflage prints on character clothing

Three camouflage prints on the buddies' clothes are redrawn at twice the resolution, two of them
with another pattern: the clothes the new playable women wear among them.

## How

Three textures in `graphics/actors/_textures/`, replaced whole (256x256 -> 512x512 DXT1, 43,864 ->
131,232 bytes, saved without mipmaps; take them from the archive's `patch.dat`):

| Texture | Base | Mod | Materials reading it (base `worlds.dat`) |
|---|---|---|---|
| `c_cm_camo_08_d.xbt` | chocolate-chip desert | digital desert | `FLORA_CARGO_PANTS2`, `JOAQUIN_CARBONELL_PANT`, `FEMCIV_HEADBAND` |
| `camo_pattern_d.xbt` | soft woodland | crisp M81 woodland | `OLIVERTAMBOSSA_JACKET`, `OLIVERTAMBOSSA_PANT` |
| `print_camo_d.xbt` | grey urban digital | darker urban digital | `FLORA_HOLSTER`, `MICHELE_HOLSTER`, `ANTOKANKARAS_JACKET`, `ARTURO_PANT` |

## Depends on

Nothing. Flora's trousers and both women's holsters are part of the first-person bodies of
`player-roster-women`, which is where the player sees them most.

## Uncertain

- The materials were found by the texture path in the base `.xbm` files; other users (sector
  objects) were not searched. Not a line of the readme.
