---
title: One cracked-bark texture on most tree trunks
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "graphics/vegetation/_common/**"
exclude: []
requires: []
verified: diff
---

# One cracked-bark texture on most tree trunks

Seven different trunk textures and one trunk specular map are replaced by one grey, deeply cracked
bark, so acacias, slim trees, busted trunks and the large base trunks all wear the same bark. The
headers the mod gave these files point at the wrong companion files, which probably shows at close
range.

## How

Fifteen whole-file replacements under `graphics/vegetation/_common/`:

| Files | Base game | Mod |
|---|---|---|
| `barks_official/{acacia01,bark_slim,barkbusted1,basetrunk_l,basetrunk_m}_d.xbt` | five different barks, 256² DXT1 | one image, byte-identical in all five, 256² DXT5 |
| the same five `_d_mip0.xbt` | 512² DXT1, 1 level | one image in all five, 512² DXT5, 10 levels |
| `barks_official/basetrunk_o_d.xbt`, `basetrunk_o_d.xbt`, `basetrunk_o_s.xbt` | two different base-trunk colour maps (`_d`; the `_common` one a smooth, tan, weathered trunk) and the dark specular map (`_s`), 256×512 DXT1 | one grey cracked-bark image in all three, 256×512 DXT5 |
| `basetrunk_o_d_mip0.xbt`, `basetrunk_o_s_mip0.xbt` | 512×1024 DXT1, 1 level | one image in both, 512×1024 DXT5, 11 levels |

`basetrunk_o_s` is a specular map, and it now holds the colour image. Trunks using it get a
mid-grey specular mask where the base game's was nearly black, so they shine more.

### The headers name other files' companions

The mod's `.xbt` headers were borrowed from other textures. In a base file the header names the
`_mip0` companion that holds the top level, and the streaming loader follows that path
(`docs/docs/file-formats/xbt.md#header`):

- The five `barks_official/*_d.xbt` name `graphics\vegetation\_common\barks_official\acacia01_n_mip0.xbt`,
  the acacia's normal map, 512² DXT5.
- The three `basetrunk_o_*` base files name `graphics\vegetation\_common\barks_official\basetrunk_w_n_mip0.xbt`,
  another normal map, 512×1024 DXT5.

Both donors are exactly twice the base size and in the same codec, so they load as a valid top
level. If the engine follows the header, these trunks draw a normal map as their colour (and, for
`basetrunk_o_s`, as their specular) when close enough to need the top level, and the mod's own seven
`_mip0` files are never read. The five barks' `_mip0` files also carry headers naming an unrelated
file (`graphics\terrain\water\watercloud_n_mip0.xbt`), which a companion should not need.

## Uncertain

- Not seen in game. Whether the close-range texture is really the borrowed normal map rests on the
  `.xbt` page's account of the header, not on a test of these files.
- The source of the bark image is not identified.
