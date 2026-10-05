---
title: Frank Bilders' trousers get a working material
kind: component
bundle: fixes
claims: []
status: located
systems: [player, graphics]
match:
  - "graphics/_materials/ycloutier-m-2008061951880468.xbm"
exclude: []
requires: []
verified: diff
---

# Frank Bilders' trousers get a working material

The trousers of the playable Frank Bilders are drawn with a proper normal map and shading. Not a
line of the readme.

## How

`graphics/_materials/ycloutier-m-2008061951880468.xbm`, in the base game `C_AVATAR_PANT_ASCOPY_ASCOPY`
and used only by `frankbilders_avatar.xbg` (the model of `player.MainCharacter.PawnPlayer.Frank_Bilders`),
is replaced whole by a byte-for-byte copy of `ycloutier-m-2007080249627347.xbm`, Marty Alencar's
`MARTYALENCAR_PANTS`.

The base material has its texture slots crossed: its `NormalTexture1` is the trousers' diffuse
texture `c_cm_pant_d.xbt`, its `BloodTexture` a specular map and its `PrintTexture` a dirt map. The
copy uses `c_cm_pant_n.xbt` as the normal map, `c_cm_dirt_01_d.xbt` for blood, drops the print
layer, and brings Marty's colours (`BaseColor1` (0.353, 0.345, 0.306), `BaseColor2`
(0.157, 0.133, 0.102)), `SpecularPower` 7 and `LogicalMaterialId` 1 (was 10).

## Uncertain

- That the base material is a mistake is inferred from the slots; the in-game difference is not
  checked. `LogicalMaterialId` may also change the surface's impact effects.
