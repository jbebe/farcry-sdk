---
sidebar_position: 11
---

# `.nvm` — Navigation Mesh

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server`. Covers the level-file/sector-file container
structure, the per-sector header, and the full field-order layout of a sector's content (node graph,
vertex positions, cover points, obstacles, spatial index) — but not yet the byte layout inside those
per-element classes themselves (`CNavMeshNode`, `CNavCover`, etc. — see Unknowns). Before this pass,
`.nvm` had no reverse-engineering work behind it at all — see [the file
manifest](../modding/file-manifest.md#6-navigation-mesh-nvm--locked), where it was the one format with
a "Locked" status and zero RE.
:::

Confirmed built on the open-source **Recast** navmesh library (community-reported, via a leaked
internal build-tool plugin list — `RecastNavmeshCompiler`/`Exporter`; not independently re-verified
here). This page covers Dunia's own file/header structure wrapping whatever Recast-derived mesh data
lives inside a sector — not Recast's own format.

## A two-tier file scheme, unlike every other per-sector format

Every other per-sector format documented so far (`.sdat`, `.srl`, `.zsr`) packs one physical file per
sector, addressed by a flat or 2D index. `.nvm` is structured differently: one **level file**
(`nv\nv.nvm`) holds a header plus a per-sector descriptor table, and — depending on a mode flag read
from that header — the actual sector mesh data lives in **separate satellite files**,
`nv\sectors\nv_<index>.nvm` (decimal index, no padding), each loaded independently.

Both paths are built by `CNavMeshLevel::MakeSectorFileName(name, index, bool)` (`0x09a0c590`), which
switches format entirely on its bool argument:
- `true` → `<root>nv\nv<index>_<index>.nvm` (same index formatted twice — not two coordinates, despite
  the two `%d`s)
- `false` → `<root>nv\sectors\nv_<index>.nvm` — the per-sector satellite file path

## What actually ships

Measured against the retail archives, the two tiers live in **different trees**, and both sit under
`nv\sectors\` — the level file is not at `nv\nv.nvm` as the path builder's alternate branch suggests:

| Tier | Path | Count |
|---|---|---|
| Level file | `worlds\<world>\generated\nv\sectors\nv.nvm` | 26 — every multiplayer world, both campaign worlds, `tmpla` |
| Sector satellite | `levels\<level>\generated\nv\sectors\nv_<sectorId>.nvm` | 5120 — **campaign levels only**, 256 each across 20 levels |

No multiplayer level, no `tmpla`, and no `ige_map` has a single sector satellite. Multiplayer maps
ship a level file with no sector data behind it, which is consistent with multiplayer having no AI.
The editor's own world (`ige_map`) has neither tier, so maps built in the editor carry no navigation
data at all — see [`.fc2map`](./fc2map.md).

Satellite indices are the level's own sector ids, matching `sector<sectorId>.desc.fcb` exactly.

## Level-file header, measured

The shipped level files are header-only and follow a fixed layout. Reading them back confirms the
field order recovered from `CNavMeshLevel::SerializeData`:

| File offset | Content |
|---|---|
| `0x04` | `0x4e764d68` — `hMvN` in raw byte order, the same tag the per-sector header writes |
| `0x18`, `0x1c` | world extent, `f32` ×2 |
| `0x40`, `0x44` | sector grid dimensions, `u32` ×2 |
| `0x48` | mode flag — `2` in every shipped file |
| `0x4c` | sector count |
| `0x50` | `u32[sectorCount]` descriptor table |
| — | 24 trailing bytes |

`0x50 + 4 × sectorCount + 24` accounts for the file size exactly in every sample. Grid dimensions
multiply out to the sector count, and extent divided by grid dimension is **64 world units per
sector** in all of them:

| World | Extent | Grid | Sectors | Size |
|---|---|---|---|---|
| `tmpla` (editor world shape) | 512 | 8×8 | 64 | 360 |
| `mp_16_airbase` | 640 | 10×10 | 100 | 504 |
| `world1` | 5120 | 80×80 | 6400 | 25704 |

Two observations complicate the satellite-loading path described above. The mode flag reads `2`
(single-file) rather than `3` (per-sector satellites) in every shipped level file, and the descriptor
table is **entirely zero** in all of them — including `world1`, which has 5120 satellites on disk.
Whatever locates those satellites at run time therefore does not appear to be the descriptor table as
serialized, and the level file functions in practice as a grid/extent declaration.

## Versioned serialization, not a size split

Every `SerializeData`-family function in this format (level, sector, and sector-content) is a
**dual-direction archive function** in the CryEngine-lineage "serialize" idiom: the same code path
handles both save and load, branching on the `CNavArchive`'s own internal write-mode flag rather than
being split into separate reader/writer functions.

Every one of these functions also guards blocks of fields behind a check on `CNavArchive+0x48` — an
integer carried by the archive itself, not the sector or level. Initially this looked like a size
threshold ("small vs. large navmesh"), but `CNavMeshSector::SerializeDataContent` alone checks it
against **eight different graduated values** (`0x10000`, `0x125ff`, `0x13000`, `0x13200`, `0x133ff`,
`0x13400`, `0x134ff`, `0x13600`) to decide whether to read/write successive optional field blocks. That
many distinct thresholds only makes sense as a **stored format version number**, each threshold marking
a point where a new field or array was added to the format — standard incremental-versioning
serialization, not a small/large split. Treat `CNavArchive+0x48` as "format version" going forward.

## `CNavMeshLevel::SerializeData` — the level-file header

`CNavMeshLevel::SerializeData(CNavArchive&)` (`0x09a0e210`) reconstructed field order for the
level-file header, in the newest-version branch:

```
u32  field_0x54  ┐
u32  field_0x58  │
u32  field_0x5c  │  six header words, semantics not decoded — populated from a shared
u32  field_0x60  │  zero-initialized global on old-format archives, read individually on new ones
u32  field_0x64  │
u32  field_0x68  ┘
u32  field_0x6c        (version >= 0x10000 only)
u32  field_0x70        (version >= 0xffff only)
--- CNavMeshLevel::InitSectorMatrices(this) runs here, presumably deriving grid dimensions from the above ---
u32  modeFlag           (version >= 0x10000 only; older archives default this to 0) — 0/2 both mean
                         "single-file", 3 means "per-sector satellite files" (see below)
u32  sectorCount
u32[sectorCount]  sector descriptor table — raw u32 per sector, non-zero = "this sector has data"
if modeFlag == 3:
    for each non-zero descriptor: CNavMeshLevel::LoadIndSector(index, ...) reads
    nv\sectors\nv_<index>.nvm as its own standalone CNavArchive
```

The write side mirrors this exactly: for `modeFlag == 3`, each non-null sector gets its own
`MakeSectorFileName(index, false)` path and a fresh `CNavArchive`, which the sector serializes itself
into before the resulting size/handle is recorded back into the level file's descriptor slot.

## `LoadIndSector` — per-sector load

`CNavMeshLevel::LoadIndSector(sectorIndex, CNavArchive*, buffer, size)` (`0x09a0d440`) either takes an
already-loaded buffer or opens `nv\sectors\nv_<sectorIndex>.nvm` itself via `MakeSectorFileName`, wraps
it in a `CNavArchive`, allocates a `CNavMeshSector` (`0x78` / 120 bytes), and calls its virtual
`SerializeData` (see below) to deserialize the sector's content. After a successful load it does
spatial-region culling against `CWorldRegion::Includes` (sectors outside the currently-relevant world
region get dropped via `CNavMeshSector::DeleteSector` rather than kept resident), updates two bitmask
grids at `this+0x84`/`this+0xa0` (present/pending-load flags per sector, same bit-per-sector-index
pattern seen in other systems this session), fires a `CNavMeshSector::NotifySectorEvent`, and clears
`CPathManager`'s cached pathfinding results — a loaded sector invalidates any in-flight path queries
that might have assumed it was still absent.

## `CNavMeshSector::SerializeData` — the real per-sector payload

`CNavMeshSector::SerializeData` (`0x09a21d20`) is vtable slot 0, the method `LoadIndSector` calls
through a virtual dispatch. It splits into two: `SerializeDataHeader` (`0x09a21710`) then, if that
succeeds, `SerializeDataContent` (`0x09a1e780`) — by far the richest function traced in this whole
format.

**Header** (`SerializeDataHeader`): sector id/coordinates and bounding box (already known from the
constructor), followed by two constant-looking values written unconditionally on save — `0x4e764d68`
(reads as ASCII `hMvN` in raw byte order — plausibly a per-sector magic/tag) and `0x14100` — then a
computed `GetReloadSize(sector)` value. On load, the equivalent slot is read back and compared against
the archive's version field, and the whole header read fails (returns `0`) if they disagree — a real
version/consistency check, not just informational.

**Content** (`SerializeDataContent`), in field order:

```
u16  sectorX, sectorY                     (already known from the constructor)
f32  bbox[4]                              (already known)
u32  field_0x6c                           (default 0x4f800000 = ~4.29e9, a sentinel-looking float)
u32  field_0x70, field_0x74               (a pair; falls back to CNavmeshEdition::GetInstance()'s own
                                            +0x54/+0x58 fields when unset — editor-time defaults)
u16  field_0x5c
--- CNavArchive::SetPackedVectorSettings() runs here: every vec3 below this point is quantized to
    3×int16, scaled relative to this sector's own bounding-box center/extent ---
u32  field_0x64
u32[field_0x64]              a raw (no per-element parser) block of u32s
u32  nodeCount
CNavMeshNode[nodeCount]      the actual navmesh graph — 60 bytes each, own SerializeData
f32vec3[nodeCount] → packed  quantized vertex positions (one per node, 6 bytes each on disk)
u32  coverCount
CNavCover[coverCount]        static AI cover points — 28 bytes each, own SerializeData
u32  dynCoverCount
CDynamicNavCover[dynCoverCount]  dynamic/toggleable cover points — 40 bytes each, own SerializeData
f32vec3[]  → packed          a second quantized vertex-position array (purpose distinct from the first,
                              not identified — candidate: edge midpoints or off-mesh link endpoints)
CNavMeshQTree                a spatial index over the node list, built fresh from node positions via
                              CNavMeshQTreeWriter and serialized inline — baked into the file, not
                              rebuilt at load time
--- everything below this point is version-gated (see above) and progressively larger version numbers
    unlock more of it ---
u32  obstacleCount            (version >= 0x13600)
CNavMeshObstacle[obstacleCount]  dynamic blockers — 40 bytes each, own SerializeData
f32vec3[]  → packed           additional quantized vertex arrays (version >= 0x13000 / >= 0x134ff)
CNavMeshQTree (again)         a second CNavMeshQTree is allocated and its own virtual SerializeData
                               called unconditionally at the very end — relationship to the inline one
                               above not resolved (see Unknowns)
```

`AfterLoad(sector)` runs as the final step on the read path — a post-processing hook, presumably
rebuilding runtime-only derived structures (adjacency, the live A* graph) from what was just
deserialized, before the sector is marked ready (`this[0x5e] = 0`).

This gives a genuinely complete structural map of what a navmesh sector contains: a polygon/node graph
(`CNavMeshNode`), quantized vertex positions, two flavors of AI cover point, dynamic obstacles, and a
baked spatial index — everything needed to actually decode geometry now has a named target class and a
known position in the byte stream.

## Unknowns

- The semantic meaning of the level-file header fields (`+0x54` through `+0x70`) and the sector-content
  scalar fields (`+0x5c`, `+0x64`, `+0x6c`, `+0x70`/`+0x74`) — only their storage location and
  read/write order are confirmed, not what they represent.
- The byte layout inside `CNavMeshNode`, `CNavCover`, `CDynamicNavCover`, `CNavMeshObstacle`, and
  `CNavMeshQTree` — each has its own `SerializeData`, none opened yet. This is the natural next layer:
  opening these five gets to the actual triangle/graph/cover-point field values.
- The purpose of the second quantized-vertex array, and the relationship between the inline
  `CNavMeshQTreeWriter`-built tree and the second `CNavMeshQTree` serialized unconditionally at the end
  of `SerializeDataContent` — possibly one is a full-precision editor-time tree and the other a
  runtime-optimized rebuild, not confirmed.
- What exactly selects `modeFlag` 0/2 vs. 3 (single-file vs. per-sector-satellite-files) — whether it's
  a global setting, a per-level authoring choice, or tied to the format version the same way the
  header-length gate is.
- Whether `nv\nv.nvm` (the writer's "same index twice" `MakeSectorFileName(true)` branch) is ever
  actually reached in practice, or is dead/legacy code — no confirmed caller was found using that
  branch during this pass.
