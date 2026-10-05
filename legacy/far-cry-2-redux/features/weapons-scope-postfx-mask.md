---
title: Scope post-effect mask nearly blanked
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, graphics]
match:
  - "graphics/postfx/sniperscope.xbt"
exclude: []
requires: []
verified: diff
---

# Scope post-effect mask nearly blanked

The screen mask behind the sniper scope's post effect is replaced by an almost uniformly black one,
so the effect no longer fades in around a soft oval.

## How

`graphics/postfx/sniperscope.xbt` (256x128 DXT1), replaced whole: the base mask is white with a dark,
soft-edged oval in the middle (22,024 bytes, nine mip levels); the mod's is black with a faintly
lighter, hard-edged oval (16,544 bytes, no mipmaps). Take it from the archive's `patch.dat`.

## Depends on

Nothing found. The scope views themselves are `weapons-scope-fov`, the reticle `weapons-scope-reticles`.

## Uncertain

- Which post effect samples this mask (blur, darkening or distortion around the scope) is not traced,
  so what the player sees change is not known; it may be part of the readme's "they should no longer
  float" fix (6-6-20), but nothing ties it to that line.
