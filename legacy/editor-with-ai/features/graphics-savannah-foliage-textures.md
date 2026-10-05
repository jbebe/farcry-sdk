---
title: Darker savannah leaves and grass
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "graphics/vegetation/savannah/**"
exclude: []
requires: []
verified: diff
---

# Darker savannah leaves and grass

The two savannah leaf atlases are repainted darker and more saturated, so savannah trees and bushes
read as a deeper green. The savannah grass texture keeps its look.

## How

Six whole-file replacements in `graphics/vegetation/savannah/textures/`, each a base file and its
`_mip0` companion (see `docs/docs/file-formats/xbt.md#the-top-mip-level-lives-in-a-second-file`):

| Texture | Base game | Mod |
|---|---|---|
| `leaf_savannah_01_d` | 1024² + 2048² companion, DXT3 | same sizes, DXT5 |
| `leaf_savannah_02_d` | 1024² + 2048² companion, DXT3 | same sizes, DXT5 |
| `sgrass_d` | 512² + 1024² companion (1 level), DXT3 | same sizes, DXT5; the companion now carries a full 11-level chain |

- **Leaves.** The same atlas layout and the same alpha cut-outs, branch for branch. Only the colour
  changes: the average colour drops from about (53, 68, 33) to (32, 45, 15), roughly 40% darker,
  with blue cut the most. Both resolution tiers are repainted the same way.
- **Grass.** Same layout and alpha; the average colour moves by a few levels (slightly warmer).
  The change is mostly the codec.

All three base files keep a header that names their own `_mip0` companion, so the pairs stream as
in the base game.

Nothing else references these files differently; every material that uses them (several dozen
`graphics/_materials/*.xbm`) picks the change up.

## Uncertain

- The source of the repaint is not identified.
