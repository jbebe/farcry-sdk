---
title: New bullet-hole decals
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "graphics/gfx/_decals/**"
  - "databases/generic/decal.xml#Generic[*]@iTextureVariationCount"
exclude: []
requires: []
verified: diff
---

# New bullet-hole decals

Bullet impacts on concrete, metal, wood, pebbles, plastic, tile and leather leave new marks: a
single round hole per surface, most of them photographic, in place of the base game's sets of torn,
splintered or smeared marks. Concrete and wood lose their four random variants.

## How

Ten whole-file replacements in `graphics/gfx/_decals/`, and the variant count of the three decals
whose texture used to be a strip of four. The decal types are those of
`databases/generic/decal.xml`:

| Texture | Used by | Base game | Mod |
|---|---|---|---|
| `gfxd_bullimp_concrete` | `Base.Concrete`, `Base.Default` | 512×128 DXT5, a strip of four marks | 256² DXT5, one cracked-concrete hole |
| `gfxd_bullimp_concrete_n` | the same | 512×128 DXT5, four variants | 256² ATI2 (BC5) |
| `gfxd_bullimp_metal_01` | `Base.Metal_soft` | 128² DXT5, a grey smear | 128² DXT5, a black hole in brushed metal, 6 levels instead of 8 |
| `gfxd_bullimp_metal_hard` | `Base.Metal_hard` | 64² DXT5, a soft smudge | 128² DXT5, a hole with a copper ring in painted metal |
| `gfxd_bullimp_pebble` | `Base.Pebble` | 128² DXT5 | 128² DXT5, darker and speckled, 6 levels instead of 8 |
| `gfxd_bullimp_plywood` | `Base.Wood_hard`, `Base.Wood_light` | 512×128 DXT5, a strip of four splintered holes | **64²** DXT5, one charred hole |
| `gfxd_bullimp_small_burn` | `Base.Plastic`, `Base.Small_burn` | 128² DXT5 | 64² DXT5, a dark hole |
| `gfxd_impact_tile` | `Base.Tile` | 128² DXT5, a cracked tile | 128² DXT5, a reddish hole |
| `gfxd_impact_tile_n` | `Base.Tile` | 128² DXT5 | 128² DXT5, a grey copy of the new hole |
| `gfxd_leather_hole` | `Base.Leather` | 128² DXT5, a star-shaped tear | 128² DXT5, a dark hole |

`databases/generic/decal.xml`, `iTextureVariationCount` 4 -> 1 on `Base.Concrete` (`Generic[0]`),
`Base.Wood_hard` (`Generic[12]`) and `Base.Wood_light` (`Generic[13]`). The count tells the engine
how many variants the texture is split into, so the two edits go together: with the new single-hole
textures and a count of 4, each impact would show a quarter of the hole; with the old strips and a
count of 1, all four variants at once, squashed.

The decal lifetimes the mod also changes in `decal.xml` are `graphics-decal-lifetime`.

## Uncertain

- `gfxd_bullimp_concrete_n` is ATI2 (BC5), a codec no shipped texture uses
  (`docs/docs/file-formats/xbt.md`), and `gfxd_impact_tile_n` looks like a grey copy of the new
  hole rather than a normal map. How the decal shader reads either is not checked; both may light
  the hole wrongly.
- `gfxd_bullimp_metal_hard`'s header was borrowed from the ammo crate's mask and names
  `graphics\_textures\mask\ammocrate_m_mip0.xbt` as its companion (512² DXT1, four times the
  decal's size, a different codec). The base game's decal has no companion. What the streaming
  loader does with it is not known.
- That the variant count slices the texture along its long axis is inferred from the base game's
  4:1 strips and the mod's matching edits, not traced.
- The source of the new marks is not identified.
