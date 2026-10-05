---
title: Higher-resolution grey smoke puffs
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "graphics/gfx/smokes/**"
exclude: []
requires: []
verified: diff
---

# Higher-resolution grey smoke puffs

The two generic smoke-puff sprites are replaced at twice the resolution, with fuller, billowier
shapes. The dark smoke turns from brown to neutral grey.

## How

Two whole-file replacements in `graphics/gfx/smokes/`:

| Texture | Base game | Mod |
|---|---|---|
| `gfx_smok.xbt` | 256² DXT5, a white puff | 512² DXT5, white, a rounder, denser puff in alpha |
| `gfx_darksmok.xbt` | 256² DXT5, a brown puff (average colour about (58, 51, 40)) | 512² DXT5, a grey cloud texture over the whole square (about (115, 115, 120)), its puff shape in alpha |

The shape of each puff is its alpha; the colour channels of the new dark smoke are a tiling cloud
photo, so only the alpha decides where it shows. Both headers carry no companion path, so nothing
else is loaded with them.

In `world1_deploadnewparticles.rml`, `GFX_smok.XBT` is the sprite of 92 emitters in 87 particle
systems: mostly weapon effects (muzzle and barrel smoke, 32 systems), machete and bullet impact
puffs (28), and some ambient, destruction, fire and explosion smoke. `GFX_darksmok.XBT` serves
three: `weapons.malfunction.mounted_overheat_high`, `explosions.explosives.std_propan_tank_propeller`
and `weapons.flamthrower.fireball_2`. The animated `_anm` and `static_smoke` sets most fires use are
not touched. The mod changes no emitter to use these sprites.

## Uncertain

- The source of the new sprites is not identified.
