---
sidebar_position: 17
---

# `.xbt` — Textures

:::info[Verified via reverse engineering]
Header layout traced in `Dunia.dll` (`Xbt_ParseHeader` @ `0x10339b40`) via GhidraMCP; the streaming
split below is confirmed against the shipped graphics tree. See [intro](../intro.md) for how
RE-verified and community-reported claims are distinguished on this site.
:::

An `.xbt` is a small engine header followed by a complete, fully valid `.dds` payload. Strip the
header and any DDS tool opens the rest.

## Header

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | `"TBX\0"` signature |
| 4 | 4 | `Version` — 11 in every real sample; 10 is an older, shorter variant the loader still accepts |
| 8 | 4 | `HeaderSize` — **the byte offset of the DDS payload**; the engine computes `dds = buffer + HeaderSize`, it never scans for `"DDS "` |
| 12 | 4 | `Reserved` — a bitfield the streaming loader really does consume (see below) |
| 16 | 12 | `Hash` — v11 only; leading 4 bytes are a stable per-asset id shared between a texture's resolution tiers. No traced caller reads it back |
| 28 | … | Null-terminated ASCII path, up to `HeaderSize`: the companion described below, or empty |

`Reserved` is not decoration. Of `Xbt_ParseHeader`'s five callers, four only take the computed DDS
pointer and size, but the streaming-texture loader reads `Reserved` as flags: bit `0x100` resets two
LOD-tracking fields on the resource object, and the low byte is stored and consumed later for
streaming decisions. It varies per asset (1, 2 and 4 all appear across ~130 sampled files) with no
correlation yet found to DDS format, companion presence, or naming. Because neither `Reserved` nor
`Hash` can be synthesized, there is no honest "build an `.xbt` from a bare `.dds`" path — every
header byte has to come from a real file.

## The top mip level lives in a second file

When the embedded path is non-empty it names a sibling `<name>_mip0.xbt`, and **that file holds the
texture's real level 0**. The file naming it starts one level down.

- `jungle_underbrush_hod_d.xbt` — 1024×1024 DXT1, 11 mips, header names
  `graphics\terrain\_textures\jungle\jungle_underbrush_hod_d_mip0.xbt`
- `jungle_underbrush_hod_d_mip0.xbt` — 2048×2048 DXT1, 1 mip

Stacked, that is a 2048 texture with a 12-level chain. The companion always carries exactly one
level, at exactly twice the base's dimensions, in the same DXT format.

**960 of the 1,947 textures** in `worlds/graphics` are split this way — 49%. A reader that opens only
the named file gets a texture that is correct in every respect except being half the size on each
axis, which is invisible until something is drawn close enough for that texture to fill the screen.
The split exists so the engine can drop the largest level under memory pressure without touching the
rest of the chain.

Sizes cluster tightly: base textures are mostly 256×256 (487 files) or 512×512 (91), and companions
mostly 512×512 (253) or 1024×1024 (91).

## Terrain layer textures

Every entry in a world's terrain layer table (`<world>.game.xml`, `<Layers>`) points at a diffuse
`.xbt`, and all of them are square, power-of-two DXT1. Across `world1`, `world2` and the MP maps the
same 25 textures serve all 45 layer slots.

| Base | Companion | Examples |
| --- | --- | --- |
| 1024² | 2048² | most — `jungle_underbrush_hod_d`, `savannah_undergrass_d`, `riverbed_rocky_d`, all the `_mountain_rock_` sets |
| 512² | 1024² | `urban_ground_d` |
| 256² | 512² | `stiresjunk01_d` |
| 256² | none | `desert_sand_still_d`, `desert_sand_rippled_d` |

Each layer also carries a `Tiling` in world units per repeat — 20 for most ground, 2–3 for rock, 100
for open sand — so the usual case is 2048 texels over 20 metres, about 102 texels per metre. The
two 256² sand layers are the exception, and they get their close-up detail from a normal map at a
much finer tiling instead: `desert_sand_still` pairs a `Tiling="100"` diffuse with a
`NormalMapTiling="6"` normal map.

Every layer names a `NormalMap` (DXT5, same streaming split) and most name a `SpecularMap`, each with
its own independent tiling, alongside `SpecularColor`, `SpecularIntensity`, `SpecularShininess`,
`MinSlope`/`MaxSlope`, `AltStart`/`AltEnd` and a `ProjAxis` (0 = X, 1 = Y, 2 = Z) that gives cliff
layers a sideways projection instead of a stretched top-down one.
