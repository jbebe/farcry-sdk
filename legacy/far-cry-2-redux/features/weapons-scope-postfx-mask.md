---
title: Scope post-effect mask nearly blanked
kind: component
bundle: weapons
claims: []
status: no-artifact
systems: [weapons, graphics]
match: []
exclude: []
requires: []
verified: diff
---

# Scope post-effect mask nearly blanked

The mod replaces the sniper scope's post-effect mask with an almost uniformly black one. No engine
code path reads that file (the reachability list marks it unreachable), so the replacement changes
nothing in game and an import leaves it behind.

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
