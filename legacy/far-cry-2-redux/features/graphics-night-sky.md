---
title: Smaller grey moon and round stars
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics, environment]
match:
  - "graphics/sky/**"
exclude: []
requires: []
existing: mods/sky-overhaul — halves the moon's size through the world descriptor's environment, not the texture
verified: diff
---

# Smaller grey moon and round stars

The moon is drawn smaller and grey instead of blue-white, and the stars are soft round dots instead
of four-pointed sparkles. Not a line of the readme.

## How

Two textures, replaced whole (the base copies are in `worlds.dat`):

- `graphics/sky/dome/moon.xbt`, 256x256 DXT5, same size: the moon's disc fills about 60% of the
  texture's width instead of nearly all of it, and is a grey full moon instead of a bright
  blue-tinted one. The sprite's size in the sky is unchanged, so the moon looks about 60% as wide.
- `graphics/sky/dome/stars/star_d.xbt`, 16x16 DXT1, same size: the star sprite, a cross-shaped
  sparkle in the base game, becomes a small round dot.

## Uncertain

- How bright the moon's light is does not depend on these textures; nothing in the mod's lighting
  presets is in this domain.
