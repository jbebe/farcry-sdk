---
sidebar_position: 4
---

# `.sdat` — Per-Sector Terrain Data

:::info[Verified via reverse engineering — supersedes an earlier community guess]
Traced live via GhidraMCP against **`FarCry2_server`** (the Linux dedicated-server ELF, not
`Dunia.dll`) — its fuller symbol/type table (real class names like `CSector`, `SSectorDataChunk`,
`CSceneTerrainSectorPackedData` survive) made this tractable. Covers the on-disk byte layout of
world-sector `sdN.sdat` files, the per-8×8-grid terrain sectors already catalogued at a black-box level
in [Engine Theory](../modding/engine-theory.md).
:::

## This supersedes an earlier community-sourced guess

[Engine Theory](../modding/engine-theory.md) and `tools/JackAll/src/JackAll.Core/Format/SdatHeightmap.cs`
previously encoded a Discord-derived guess: "`.sdat` is a pure 513×513 `u16` heightmap grid, no header
at all." **That is not what this binary does.** The real format is a generic chunked container with a
20-byte header, a fixed 572-byte metadata block, a fixed ~22.4KB packed-data blob (not a raw height
array), a variable-length record array, and a 20-byte tail. None of the 5 serialized blocks is a bare
513×513 `u16` grid (513×513×2 = 263,169 bytes; nothing here is that shape). The 513×513 figure was very
likely a mix-up between per-sector resolution and the whole multiplayer map's resolution (8×8 sectors ×
64 quads/sector = 512 quads across the map, +1 for the shared edge = 513 map-wide vertices — a real
number, just not the per-file one; see "Height sub-layout" below).

## Writer and reader, both confirmed

- **Writer**: `CFCXEditorDocument::ExportSDAT` (`0x08caf930`) → `ThreadedExportSDAT` (`0x08cb1ec0`) →
  `CSector::ExportSectorDataChunk` (`0x097e61e0`), which serializes via a generic `CChunkWriter`
  (`0x09c704d0`/`0x09c70520` ctor, `OpenChunk`/`AddChunkData`/`CloseChunk` at
  `0x09c706b0`/`0x09c705f0`/`0x09c70790`). Confirmed sole caller of the `"%ssd%d.sdat"` format string —
  the multiplayer world-sector terrain file, one per sector, 8×8 = 64 files per world (`sd0.sdat` …
  `sd63.sdat`, index = `col + row*8`).
- **Reader**: `CSector::Load(uchar*)` (found via `decompile_function_by_address` on a raw offset, since
  many classes declare their own `Load`). Reads back exactly what the writer produced — every
  offset/size constant matches byte-for-byte, including a round-trip integrity check.

## Byte layout

All fields little-endian (`CChunkWriter`'s constructor is called with `littleEndian=true` at the only
call site). Offsets are absolute from the start of the file.

```
0x0000  u32   ChunkType   = 0xE9001052 (hardcoded literal, not a runtime-computed hash — see below)
0x0004  u32   ChunkVersion = 7
0x0008  u32   TotalSize    = 0x14 + OwnDataSize   (no nested chunks in this file → equals file size)
0x000C  u32   OwnDataSize  = 0x5BAC + RecordCount*12   (checked by the loader; mismatch aborts load)
0x0010  u32   ChildChunkCount = 0   (always — CChunkWriter supports nested chunks generally, a sector
                                      file just never opens a second one)
0x0014  572   SSectorDataChunk header  (field table below)
0x0250  22876 Packed terrain-data blob (CSceneTerrainSectorPackedData, struct offset +4..+0x5960)
0x5BAC  N*12  Array of RecordCount 12-byte "quad LOD/hole" records (N = RecordCount, from the header)
0x5BAC+N*12  4   trailing packed-data field (round-trips to CSceneTerrainSectorPackedData+0x596c)
+4      16    tail block: 4× u32/float, round-trips to CSceneTerrainSector+0xc4/0xc8/0xcc/0xd0 —
                contiguous 4-wide pattern suggests a per-sector bounding box or height min/max/range,
                not independently confirmed
```

Total file size = `0x5BC0 + RecordCount*12` (23,488 bytes + 12 bytes/record).

`0xE9001052` is confirmed hardcoded in `__static_initialization_and_destruction_0` (`0x097e5fce`):
`*(undefined4*)PTR_Type_0a4151a4 = 0xe9001052;` — a conforming parser can treat this as a fixed magic
number with no need to reproduce whatever registration scheme originally minted it.

### `SSectorDataChunk` header (572 bytes @ file offset `0x14`)

Reconstructed from `CSector::ExportSectorDataChunk`'s own stack layout:

```
+0x000  u32   sector id/type      (*(u32*)this, i.e. CSector+0x0)
+0x004  u32   flags               (low byte = OR of two flag bytes: *(byte*)(terrainSector+0xb8) |
                                    CSector+0x2c; round-trips on load to terrainSector+0xb8 as a bool)
+0x008  f32   sector X            (world/sector position)
+0x00C  f32   sector Y
+0x010  u32   (CSector+0x24)      — another per-sector field, not identified further
+0x014  u32   0x595C (constant)   — echoes the packed-blob size below (likely a validation check)
+0x018  u32   RecordCount         — count of the 12-byte record array at file offset 0x5BAC
+0x01C  u32   1 (constant)        — unidentified; always 1 at export
+0x020  538   GetEnvSettings() snapshot, memcpy'd verbatim — a baked per-sector copy of
                                    environment/render settings, not derived from sector geometry
+0x23A  2     padding/unaccounted (572 - 0x23A = 2 trailing bytes not explicitly written)
```

### The 22,876-byte packed blob (`CSceneTerrainSectorPackedData`, file offset `0x250`)

**Not a raw height array.** `ExportSectorDataChunk` calls
`CTerrainSectorGenericCompiler::PreparePackedDataForExport` (`0x097f01f0`) immediately before
serializing this blob, which builds, from data already resident in the struct:

- Multi-resolution "hole"/quad-visibility bitmasks over a base **64×64** quad grid (4 mip levels:
  64×64, 32×32, 16×16, 8×8 — packed nibble/bit arrays at struct-relative offsets ~`0x4208` (per-quad,
  2-bit fields), `0x56b0`/`0x5410`, `0x5810`, `0x5910`) — one hole-flag per 8×8 block of the 512×512
  quad terrain (512/64 = 8).
- Per-mip-node bounding data (4 LOD levels × 16 nodes × 8 bytes) — min/max-style culling bounds, not
  confirmed further.
- The variable 12-byte record array itself (dynamic array at struct offset `0x5960`/count at `0x5964`)
  — one record per active (non-`0xff`) mask entry across all 4 mips, each:
  `[u8 maskValue][3 bytes][u16][u16][u32 [email protected]*16+quadIndex]`. Read back 1:1 by
  `CSector::Load`.

**Height sub-layout**, confirmed via `CSector::GetZApr` (`0x097ecf50`, called from `CTerrain::GetZApr` →
`FCE_TerrainManager_GetHeightAt` — a runtime height query used for collision/physics, present in the
dedicated-server binary because the server needs ground height for hit detection and vehicle physics
even with no renderer):

```
iVar2 = *(int*)(this + 0x28)              // cached pointer, == packed-blob base (file offset 0x250)
index = row*0x41 + col                     // 0x41 = 65 → confirms a 65-wide row stride
height_u16 = *(ushort*)(iVar2 + index*4)   // 2-byte height sample, 4-byte stride per grid cell
height_m   = (float)height_u16 * 0.0078125      // 1/128 — now confirmed, see below
```

It samples the 4 corners of a quad (`index`, `index+1`, `index+0x41`, `index+0x42`) and bilinearly
interpolates. **Each sector's native terrain resolution is 65×65 vertices (64×64 quads), not 513×513.**
`PreparePackedDataForExport`'s own 64-iteration mask loops (`local_290 != 0x40`) already hinted at the
same 64-wide base grid before this confirmation.

The packed blob's first `65*65*4 = 0x4204` (16,900) bytes are this height/material grid — one 4-byte
record per grid cell, row-major, row stride `0x41*4 = 0x104` (260) bytes:

```
+0x0  u16   height sample (LE), world Z = value * 0.0078125 (1/128)
+0x2  u8    normal X, encoded (b / 255) * 2 - 1   — see "Normals" below
+0x3  u8    bits 7..5 = detail-layer index 0..6; the value 7 means HOLE
             bit  4    = not written by any traced function (set in ~10% of campaign cells)
             bits 3..0 = quad mask, mirrored into the mip-mask tables
```

:::note[Corrected — the layer index is the top three bits, not the low nibble]
This page previously described `+0x3` as a low nibble holding a "material"/hole-select index 0–15.
Tracing the readers in `FarCry2_server` shows otherwise: `CSector::GetHole`,
`CTerrain::IsSectorEditorHole` and `CSector::GetSurfaceType` all evaluate `byte >> 5`, i.e. the top
three bits, giving a range of 0–7 with **7 reserved to mean hole**. The earlier reading of the
triangle-index builders is not contradicted — they consume the same byte — but the field's width and
position were wrong.
:::

### The low nibble is the quad mask

`STerrainSectorPackedData::ClearQuadMasks` clears it with `Flags &= 0xf0` and
`FillQuadMasksWith15` sets it with `Flags |= 0x0f`, so the quad mask is **bits 3..0** exactly.
`CTerrainSectorGenericCompiler::PreparePackedDataForExport` — called by
`CSector::ExportSectorDataChunk` immediately before the chunk is written — fills it: for each of the
64×64 quads it reads one nibble from a source array (two quads per source byte, high nibble first)
and ORs it into both the cell's `+0x3` and the four mip-mask tables at `0x56b0`/`0x5410`, `0x5810`
and `0x5910`. A sector with no source array gets `FillQuadMasksWith15` instead, i.e. all-15.
`ClearQuadMasks` zeroes the mip region `[0x56ac, 0x594c)`, which bounds it exactly against the
surface-type palette that follows.

The writers of the *other* fields are careful to leave this nibble alone:
`CTerrain::SetSectorEditorHole` and `STerrainSectorPackedData::SetSurfaceType` both write
`(newIndex << 5) | (old & 0x1f)`, and `SetSurfaceType` additionally refuses to overwrite a cell whose
index is already 7, so a hole is sticky once set.

`SetSurfaceType` also shows the palette is **per-sector and first-fit**: it scans the seven bytes for
the requested surface type, claims the first `0xff` (empty) slot if absent, and silently does nothing
when all seven are taken. The stored index is therefore a slot number with no meaning outside its own
sector — which is why campaign terrain only ever uses indices 0–3 (measured across all 6,400 `world1`
sectors: 71%, 24%, 4.4%, 0.09%), matching the four layers `DetailTexMask` names per sector.

For a cell that is not a hole, the layer index selects one of **seven bytes of surface-type palette
at struct-relative offset `0x594c`** (`CSector::GetSurfaceType`, which returns `0xff` for holes;
`CTerrain::GetSurfaceType` then maps that to `0`).

### Normals

Normals occupy **two separate planes**: X is interleaved into each cell record at `+0x2` above, and Y
is a standalone byte array of 4225 entries at struct-relative offset **`0x4628`**, indexed identically
(`(y - OriginY) * 0x41 + (x - OriginX)`). `CSector::GetNormal` decodes both the same way and
reconstructs Z:

```
nx = (Cells[i].NormalX / 255) * 2 - 1
ny = (NormalY[i]       / 255) * 2 - 1
nz = 1 - sqrt(nx*nx + ny*ny)
```

That is not a renormalisation and the result is not a unit vector — the engine consumes it as is.
A sector with no packed data resident returns `(0, 0, 1)` instead.

The `NormalY` array ends at `0x56a9`, immediately before the mip-mask table this page places at
`0x56b0`, and starts after the LOD0 mask region beginning around `0x4208` — so the normal plane and
the mask tables interleave in the range `[0x4204, 0x594c)` rather than either one owning it whole.
The exact boundaries between them are not pinned down.

### Writers must duplicate the shared edge

Sector grids overlap by one row and one column, and `CTerrain::SetSectorHeightFixed` writes the same
height into **up to three sectors**: the owning one, plus the sector to the left when `x % 64 == 0`,
plus the sector above when `y % 64 == 0`. Any tool that edits heightmaps offline has to reproduce
that duplication or the map will show seams at sector boundaries.

## `CChunkWriter` — the generic container

Independent of `.sdat` specifically: `CChunkWriter` is generic nested-chunk infrastructure (constructor
`0x09c704d0`, `OpenChunk`/`AddChunkData`/`CloseChunk`). Each chunk record is a 20-byte header (`type,
version, totalSize, ownDataSize, childCount`, all u32) that a reader can skip or recurse through without
knowing the payload shape, with `totalSize` rolled up recursively into the parent on `CloseChunk`. Byte
order is controlled by a constructor bool (`true` = little-endian passthrough, confirmed for `.sdat`;
`false` would byte-swap all 5 header fields to big-endian on close — dead code for every call site
examined so far, implying this container may be shared with other platforms' builds). Not cross-checked
whether [`.fcb`](./fcb.md) reuses this same class — its header as documented there doesn't obviously
match this 20-byte shape, so likely a separate format.

## Sibling per-sector files: `.srl` and `.zsr`

Every world sector has two companion files alongside its `.sdat` (a hash-list count match confirmed
earlier: 14,964 of each across the whole install — `generated\worldsectors\sectorN.srl` and
`generated\worldsectors\zonesectorN.zsr`, versus `generated\sdat\sdN.sdat` in a sibling folder, not
literally co-located). Unlike this page's chunked container, both turn out to be raw fixed-size
per-sector memory dumps with no header at all — see [`.srl`/`.zsr`](./srl-zsr.md) for the full writeup.

## Terrain layers and `DetailTexMask`

Terrain textures come from a **layer table that is global to the game**: `world1`, `world2` and the
editor's own `ige_map` all carry a byte-identical 45-entry list, so a layer index means the same
texture in every level. `C3DEngine::LoadTerrainLayersFromXML` (`0x09822ab0`) reads it from the
world's `<name>.game.xml` — `<Layers>` of `<Layer>` elements carrying `Name`, `ProjAxis`, `Texture`,
`Tiling`, `NormalMap`, `SpecularMap`, `HeightMap`, `SurfaceTypeID` and friends — and **a layer's
index is simply its position in that list**, passed as the first argument to `STerrainLayer`'s
constructor.

The table is the `<Layers>` element directly under the file's `WorldDescriptor` root, and only that
one. `world1.game.xml` holds 1,725 `<Layer>` elements in total; the other 1,680 sit under
`MissionsDef` and carry neither `Texture` nor `SurfaceTypeID`. Collecting `<Layer>` elements by a
document-wide search instead of by that parent shifts every layer index.

The same file's `<Environment>` block mixes GUID references into `<world>.managers.fcb` (the
`Lighting`/`Fog`/`Sky` preset slots, whose `CEnvironment*` objects have no decoded field names) with
a handful of literal, immediately usable values:

```xml
<DefaultEnvSettings DefaultStormFactor="0" DefaultHour="11" DefaultMin="30" />
<Fog Color="202,219,230" Start="0" End="400" FogAmount="0.8" />
<Camera ViewDistance="1024" />
```

Two parsing traps: colours here are 0–255 integers while the `<Layer>` colours in the same file are
0–1 floats, and sibling elements carry literal `f` suffixes (`<CurvedHorizon Start="500.0f">`) that a
plain float parse rejects. The `<Fog>`/`<Camera>` line is identical across every world examined
(`world1`, `world2`, the MP maps); `DefaultHour` varies per world.

`sector#.desc.fcb`'s `DetailTexMask` is not a hash despite the field's FCB type tag: it is **four
byte-sized layer indices packed into a `u32`, with `0xFF` for an unused slot**, naming the (up to)
four textures that sector blends. `CSector::GetDetailTexMask(int)` reads one. The unused slot is
always the **low** byte: `0x0D0F29FF` names layers 41, 15 and 13. Measured over a spread of 117
`world1` sectors, the low byte is unused in only **42%** — most sectors do use all four — and the
high byte is never unused:

| slot | byte | unused | what it holds |
|---|---|---|---|
| 3 | high | 0% | ground layer |
| 2 | | 0.9% | ground layer |
| 1 | | 5.1% | the `_Y` rock projection where the sector has cliffs |
| 0 | low | 41.9% | the `_X` rock projection, paired with slot 1 |

Slots 0 and 1 are consistently consecutive indices — `44/43`, `36/35`, `26/25` — which the layer
table shows are the `Mountain_Rock_X` and `Mountain_Rock_Y` entries of one biome.

The matching weights live in `generated/sdat/atlas#_mask.xbt` — 128×128 DXT1, one atlas per 2×2
sectors, so one texel per world unit. `atlas#_color.xbt` and `atlas#_diffuse.xbt` share the atlas's
dimensions and layout; `sd#_shadow.xbt` is per sector rather than per atlas. `atlas#_diffuse.xbt` is
the baked albedo distant ground is drawn from instead of the tiled detail — see
[`.xbt`](xbt.md) for the blend distances and for undoing the per-sector transpose in place.

### `sd#_shadow.xbt` carries the sun's angle — and the engine multiplies it in anyway

Correlating the baked value against `N·L` over 38,440 samples from 10 `world1` sectors, across a grid
of candidate sun directions:

| candidate | correlation |
|---|---|
| best fit: azimuth 270°, elevation 10° — `(-0.985, 0, 0.174)` | **0.768** |
| straight up `(0, 0, 1)` | -0.018 |

A correlation that high means the bake carries the sun's angle. An earlier version of this section
concluded from that alone that the bake is a full lightmap and that multiplying `N·L` on top would
apply the sun twice — but the correlation cannot actually separate a lightmap from a self-shadow
term, because slopes facing away from the sun are both dark *and* self-shadowed. What settles it is
the engine's own terrain pixel shader (`shadersobj/engine/shaders/obj10`, RDEF names intact): its
shadow map **scales the light colour while `saturate(N·L)` still shades the surface** —

```
colour = albedo * (hemisphereAmbient + saturate(N·L) * _LightColor * shadow) + specular
```

— so the bake is best read as a self-shadow/occlusion term over an analytic sun, not as a
replacement for it.

The fit also re-confirms the transpose independently — reading the shadow texels as stored scores
only 0.319, against 0.768 for the transposed reading.

The best-fit sun sits near the horizon in the **west**, which is worth knowing before inventing a
direction: JackAll had been lighting terrain from `(0.4, 0.3, 0.85)`, roughly the opposite, which
scores **-0.50**. Anti-correlated shading and a lightmap darken each other everywhere instead of
agreeing, which is what made textured terrain look muddy.

:::note[Open]
Elevation 10° does not square with the `DefaultHour=11.5` the world descriptor declares, where the
sun should be high. Either the bake includes terrain-cast horizon shadowing that biases the fit low,
or the bake is not from the default time of day. Not resolved.
:::

### Three channels, four layers

The mask has only three channels for a sector's four slots, so **the fourth weight is implicit**:
whatever `1 - (r + g + b)` leaves over, belonging to the low byte's layer. Sampling 65,616 DXT1
block endpoints across `world1`:

- **85%** sum to roughly full (232–280 of 765), so the three explicit weights carry those texels and
  the implicit one is zero
- **2.4%** are near black (under 24), meaning that texel is essentially **all** implicit layer
- 1% exceed full, so a renderer must still normalise by the total rather than trusting the sum

An earlier version of this page claimed the channels "sum to 255 at every texel". That is wrong, and
the near-black population is exactly the cliff faces: a black mask texel means solid rock, not
absence of data. Treating the mask as three normalised weights leaves those texels with no layer at
all, which is why JackAll drew flat grey patches on hillsides until the implicit fourth weight was
added.

The atlas number is not the sector id. `CSector::GetFilePaths` computes it as
`2 * S * (row / 2) + (col / 2) * 2` for a world `S` sectors wide — equivalently
`(row & ~1) * S + (col & ~1)`, the id of the block's lowest-numbered sector. So a 16×16 level cell
has 64 atlases, and atlas numbers step by 2 across a row and by `2 * S` down a column.

### Each sector's square is stored transposed

Within an atlas the four sectors occupy their natural quadrants — the block's lower-left sector in
the lower-left 64×64, and so on — but **each sector's own 64×64 square is stored transposed**, with
its two axes swapped. Reading a texel for a world position therefore means swapping the coordinates
inside the quadrant and leaving the quadrant itself alone:

```
tile      = floor(world / 128) * 128     // the atlas this position falls in
quadrant  = floor((world - tile) / 64)   // 0 or 1 on each axis
local     = world - tile - quadrant * 64
texel     = tile + quadrant * 64 + local.yx
```

Established by reassembling a campaign cell's 64 `atlas#_color.xbt` images under all 64 combinations
of a per-sector transform and a quadrant permutation, and measuring discontinuity across sector
boundaries. The transpose with quadrants left in place is the only combination that makes rivers and
roads run unbroken across the cell; it scores 1.58 against 2.14 for the next best. The colour atlas
is the right image to test against because it is continuous terrain imagery, whereas mask weights are
relative to each sector's own `DetailTexMask` layers and legitimately jump at a boundary. Layer names ending `_X`/`_Y`/`_Z` are one texture projected
down different axes; the editor palette carries them as a single entry with `ProjectionX`/
`ProjectionY` attributes, so they collapse when mapping to it.

The editor writes the identical structure with a single global set — every cooked sector of a stock
editor map carries `DetailTexMask = 09 FF FF FF` (layer 9, `Savannah_Undergrass`). The four-texture
ceiling in [`.fc2map`](./fc2map.md) authoring is therefore an editor limit; the engine already varies
its four per sector.

## Unknowns

- ~~Exact value of the height scale constant~~ — **resolved**: it is `0.0078125` (1/128), appearing
  as an inline constant in `CTerrain::GetZ`, `CTerrain::GetSectorHeightFloat`, `CSector::GetZApr` and
  `CSector::ComputeMinMaxZ`. This matches the indirect corroboration recorded earlier (flat editor
  terrain stores 2048 and objects rest at z=16).
- ~~Byte `+0x2` of each 4-byte grid cell~~ — **resolved**: it is normal X, paired with a second plane
  at `0x4628`. See [Normals](#normals).
- ~~The low five bits of the `+0x3` flags byte~~ — **partly resolved**: bits 3..0 are the quad mask
  (see [The low nibble is the quad mask](#the-low-nibble-is-the-quad-mask)). Bit 4 remains unclaimed
  by any traced reader or writer; it is set in roughly 10% of campaign cells.
- The exact boundaries between the `NormalY` plane and the mip-mask tables inside
  `[0x4204, 0x594c)`. `ClearQuadMasks` zeroes `[0x56ac, 0x594c)`, which bounds the mip region from
  below.
- The 16-byte tail block's exact meaning — shape suggests a bounding box or height min/max/range, not
  confirmed.
- `SSectorDataChunk+0x010` and `+0x01C`'s exact semantics — both round-trip correctly but purpose wasn't
  identified beyond "some per-sector field" / "constant 1".
- No real `.sdat` sample exists in this repo to test a parser against — everything above is
  static-analysis-derived and internally cross-validated between writer and reader, but not checked
  against real file bytes.
