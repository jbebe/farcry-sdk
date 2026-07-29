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
height_m   = (float)height_u16 * DAT_0a15babc   // scale constant, address confirmed but its float
                                                  // bytes weren't read — see Unknowns
```

It samples the 4 corners of a quad (`index`, `index+1`, `index+0x41`, `index+0x42`) and bilinearly
interpolates. **Each sector's native terrain resolution is 65×65 vertices (64×64 quads), not 513×513.**
`PreparePackedDataForExport`'s own 64-iteration mask loops (`local_290 != 0x40`) already hinted at the
same 64-wide base grid before this confirmation.

The packed blob's first `65*65*4 = 0x4204` (16,900) bytes are this height/material grid — one 4-byte
record per grid cell, row-major, row stride `0x41*4 = 0x104` (260) bytes:

```
+0x0  u16   height sample (LE) — see GetZApr scaling above
+0x2  u8    unidentified — not read by any traced function
+0x3  u8    low nibble = "material"/hole-select index (0-15), consumed by the LOD0/other-LOD
             triangle-index builders (CreateLOD0TrianglesFromPackedData @ 0x097ee4a0,
             CreateOtherLODTrianglesFromPackedData @ 0x097ef2d0) to choose a triangulation/restart
             pattern per quad; also OR'd into by PreparePackedDataForExport from a separate
             live-sector hole-mask source
```

The remaining struct range `[0x4204, 0x4208)` (4 bytes, gap before the mip-mask tables) is unaccounted
for — likely padding/alignment.

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

## Unknowns

- Exact value of the height scale constant `DAT_0a15babc` — address confirmed via PIC-relative
  disassembly, but its float bytes weren't read (no raw-memory-read tool available in this GhidraMCP
  setup). Treat the ~1/128 (`0.0078125`) figure already used in `SdatHeightmap.cs` as provisional until
  confirmed against a real file or memory dump.
- Byte `+0x2` of each 4-byte grid cell (between the height u16 and the material/hole nibble at `+0x3`)
  — no traced function reads it. Candidates: a second nibble pair, a normal/slope byte, or padding.
- The 16-byte tail block's exact meaning — shape suggests a bounding box or height min/max/range, not
  confirmed.
- `SSectorDataChunk+0x010` and `+0x01C`'s exact semantics — both round-trip correctly but purpose wasn't
  identified beyond "some per-sector field" / "constant 1".
- No real `.sdat` sample exists in this repo to test a parser against — everything above is
  static-analysis-derived and internally cross-validated between writer and reader, but not checked
  against real file bytes.
