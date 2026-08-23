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

**A replacement may change the dimensions.** The pair is "companion at twice the base", not a fixed
size: a weapon's 512² base and 1024² companion were rebuilt at 1024² and 2048², keeping the original
headers, and the engine loaded them. Nothing in the header carries a size — the loader reads it from
the DDS.

Sizes cluster tightly: base textures are mostly 256×256 (487 files) or 512×512 (91), and companions
mostly 512×512 (253) or 1024×1024 (91).

## What a shipped texture looks like

:::info[Measured over the graphics tree]
4,283 of the 4,289 `.xbt` outside `sdat\` decode standalone; the other six need a companion that is
not beside them. The terrain layers under `sdat\` are excluded — they are
[two 16-bit channels](#terrain-layer-textures), not colour.
:::

| Property | Count |
|---|---|
| a power of two on both axes | **4,283 of 4,283** |
| a multiple of four on both axes | 4,283 of 4,283 |
| square | 3,216 |
| larger than 2048 on either axis | **0** — the largest shipped is 2048x2048 |

| Codec | Count |
|---|---|
| DXT1 | 2,842 |
| DXT5 | 1,315 |
| DXT3 | 124 |
| uncompressed | 2 — both sky domes |

None of this is enforced by the tools. A block compressor pads an odd size rather than refusing one,
so a 300x300 texture builds and loads; what it does not do is halve cleanly through a mip chain. The
unanimity is worth knowing precisely because nothing checks it.

**A replacement may change dimensions.** The sawed-off's 512²/1024² pair was raised to 1024²/2048²
and loaded fine — the `_mip0` relationship is "twice the base", not a fixed size.

**DXT1 carries one bit of alpha.** A soft gradient dropped into a DXT1 slot becomes a hard cutout,
and nothing in the material can change the codec, so the choice is the slot or the edge.

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

Every layer names a `NormalMap` (DXT5, same streaming split) and most name a `SpecularMap`, each with
its own independent tiling, alongside `SpecularColor`, `SpecularIntensity`, `SpecularShininess`,
`MinSlope`/`MaxSlope`, `AltStart`/`AltEnd` and a `ProjAxis` (0 = X, 1 = Y, 2 = Z) that gives cliff
layers a sideways projection instead of a stretched top-down one.

## What `Tiling` counts

The terrain pixel shader builds a layer's UV as **world XY × `_DetailUVScaling[layer]`**, with V
negated; the float4's `.x`/`.y`/`.z` are the diffuse, normal and specular scales, matching the three
tilings in the XML. The vertex shader supplies that world XY as the vertex's integer grid coordinate
plus the sector's offset, so the UV is anchored to world space, not to the sector's own 0–1 UV — that
one is a separate TEXCOORD, used for the mask, colour and shadow atlases.

`Tiling` is **not** world units per repeat. It is repeats per sector, so the period is
`64 / Tiling` world units:

| Layer | `Tiling` | Period | Sanity check |
| --- | --- | --- | --- |
| `Jungle_Underbrush_Dense` | 20 | 3.2 m | ~640 texels/m off a 2048 texture |
| `Misc_Tire` (`stiresjunk01_d`) | 30 | 2.1 m | tyres come out 0.7 m across |
| `Jungle_Urban_Ground` | 20 | 3.2 m | embedded stones 13 cm |
| `Desert_CrackedEarth` | 20 | 3.2 m | dried-mud cells 27 cm |
| `Jungle_Mountain_Rock_*` | 2 | 32 m | a cliff face, not a 2 m tile |
| `Desert_Sand_Still` | 100 | 0.64 m | fine grain; the dunes come from its `NormalMapTiling="6"` → 10.7 m |

:::note[Community-reported]
The 64 is inferred, not read out of the engine. `Tiling` reaches the sector's static shader data
untouched — `C3DEngine::LoadTerrainLayersFromXML` → `STerrainLayer` (offsets 0x18/0x24/0x30) →
`CSector::InitializeLayers` — so the conversion to `_DetailUVScaling` happens in renderer code not
yet located in either binary. What supports 64 is measurement: reading `Tiling` as world units per
repeat makes those tyres 10 m across, those stones 0.8 m and those mud cells 1.7 m, and the engine's
own baked far-field albedo (below), which is exactly one texel per world unit, carries no periodicity
at a 20-texel lag that a 20-unit repeat could not hide.
:::

## Far terrain is a different texture entirely

`atlas<id>_diffuse.xbt` is a baked albedo of the blended detail at one texel per world unit, one
atlas per 2×2 sectors (128×128 DXT1, same dimensions and layout as `_mask` and `_color`). The pixel
shader samples it through `DiffuseSampler` at the sector UV and, when the `BlendDetail` flag is set,
lerps from the tiled detail into it by view distance:

```
t      = saturate(viewDistance * MaterialLODParams.x + MaterialLODParams.y)
ground = lerp(bakedAlbedo, detailBlend, t)
```

So distant ground in Far Cry 2 is not the detail textures at a high mip — it is this bake, which is
why the tiling never repeats into a visible pattern however far you look. Note the bake already
carries the `_color` tint: the shader multiplies the colour atlas into the detail path only.

The two distances come from the `<Terrain>` block of `engine\settings\defaultrenderconfig.xml`:

| Quality | `TerrainDetailBlendViewDistance` | `TerrainDetailViewDistance` |
| --- | --- | --- |
| low | 10 | 20 |
| medium | 64 | 200 |
| high / veryhigh / ultrahigh | 64 | 512 |

Reading them as "full detail out to the first, gone by the second" makes all three profiles sensible
and matches the community observation that raising the blend distance costs performance — which only
holds if it is where detail ends rather than the width of a fade. The mapping onto
`MaterialLODParams` itself has not been traced.

### The atlases are stored transposed, and it can be undone in place

Every per-sector square the cooker writes is stored transposed — the texel at (x, y) inside a
64-texel sector holds what belongs at (y, x). A reader can swap at sampling time, but then the
hardware can never filter or mip the texture, because neighbours in memory are not neighbours in the
world. That matters most for `_diffuse`, which is what the whole distance draws from.

Undoing it up front is lossless and needs no decode: transposing a square of DXT1 blocks is a move of
whole blocks plus a mirror of the 4×4 selector grid inside each, and the two endpoints do not care
where the block sits. It works down the mip chain as far as a sector is still one block across —
level 4 for a 64-texel sector.
