---
sidebar_position: 11
---

# `.nvm` — Navigation Mesh

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server`. Covers the level-file/sector-file container
structure, the per-sector header, the full field-order layout of a sector's content (node graph,
vertex positions, cover points, obstacles, spatial index) and the on-disk bytes of `CNavMeshNode` —
but not the byte layout inside the other per-element classes (`CNavCover`, etc. — see Unknowns). See
also [the file manifest](../modding/file-manifest.md#6-navigation-mesh-nvm--locked).
:::

Built on the open-source **Recast** navmesh library (community-reported, via a leaked internal
build-tool plugin list — `RecastNavmeshCompiler`/`Exporter`; not independently verified). This page
covers Dunia's own file/header structure wrapping whatever Recast-derived mesh data lives inside a
sector — not Recast's own format.

## A missing navmesh crashes the engine, it does not merely disable AI

The in-game editor's cooker emits no `.nvm` at all — no `ige_map` or `mp_*` level ships one, because
multiplayer has no AI. Placing campaign AI archetypes into an editor
map therefore produces a world where the AI system runs against no navigation data.

Observed live: a converted campaign region carrying 34 `enemy_archetypes` NPCs and 226
`STP_archetypes` smart-terrain points crashes the host **~30 seconds after load with no user input**,
deterministically. The same map with those entities removed and all 1,073 remaining objects intact
runs indefinitely.

The crash is a read access violation at `Dunia.dll+0x498d66` (Steam build), inside a record-copy loop
reached from an AI-perception routine — the one calling
`CAIObjectSystem::EndAIObjectIterator<CPawnAgent>` and issuing randomised raycasts. When the editor
host is a .NET process, this surfaces misleadingly: the managed frame beneath the engine puts
coreclr's SEH handler on the dispatch chain, that handler faults on null while handling the first
exception, and Windows blames `coreclr.dll` instead of Dunia. The original `EXCEPTION_RECORD` is
still recoverable from the crash dump's stack.

This raises navmesh generation from a quality issue to a hard prerequisite for any editor map that
places actors. The generator is already compiled into the engine (`CNavmeshGenerator`,
`CNavMeshSectorWriter`, `BuildNavMeshLevel0`); there is simply no `FCE_Nav*` export.

## A two-tier file scheme, unlike every other per-sector format

Every other per-sector format (`.sdat`, `.srl`, `.zsr`) packs one physical file per
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
integer carried by the archive itself, not the sector or level. It is not a size threshold ("small
vs. large navmesh"): `CNavMeshSector::SerializeDataContent` alone checks it against **eight different
graduated values** (`0x10000`, `0x125ff`, `0x13000`, `0x13200`, `0x133ff`, `0x13400`, `0x134ff`,
`0x13600`) to decide whether to read/write successive optional field blocks. That many distinct
thresholds only makes sense as a **stored format version number**, each threshold marking a point
where a new field or array was added to the format — standard incremental-versioning serialization.
`CNavArchive+0x48` is the "format version" throughout this page.

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
pattern seen in other systems), fires a `CNavMeshSector::NotifySectorEvent`, and clears
`CPathManager`'s cached pathfinding results — a loaded sector invalidates any in-flight path queries
that might have assumed it was still absent.

## `CNavMeshSector::SerializeData` — the real per-sector payload

`CNavMeshSector::SerializeData` (`0x09a21d20`) is vtable slot 0, the method `LoadIndSector` calls
through a virtual dispatch. It splits into two: `SerializeDataHeader` (`0x09a21710`) then, if that
succeeds, `SerializeDataContent` (`0x09a1e780`) — the richest function in this format.

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
u32  field_0x64                           (version >= 0x13200)
u32[field_0x64]              the link block: every node's neighbours, addressed from the node
u32  nodeCount
CNavMeshNode[nodeCount]      one triangle each — 48 bytes on disk, own SerializeData
u32  vertexCount             its own count, unrelated to nodeCount
f32vec3[vertexCount] → packed  the triangles' corners, 6 bytes each on disk
--- obstacles appear only on versions in [0x10000, 0x13600); retail 0x14100 skips the block ---
u32  obstacleCount
CNavMeshObstacle[obstacleCount]  dynamic blockers — 40 bytes each, own SerializeData
f32vec3[]  → packed           additional quantized vertex arrays (version >= 0x13000 / >= 0x134ff)
--- both paths rejoin here ---
u32  coverCount
CNavCover[coverCount]        static AI cover points — 28 bytes in memory, own SerializeData
u32  dynCoverCount                        (version >= 0x13400)
CDynamicNavCover[dynCoverCount]  dynamic/toggleable cover points — 40 bytes in memory, own SerializeData
f32vec3[]  → packed          a second quantized vertex-position array (purpose distinct from the first,
                              not identified — candidate: edge midpoints or off-mesh link endpoints)
CNavMeshQTree                a spatial index over the node list, built fresh from node positions via
                              CNavMeshQTreeWriter and serialized inline — baked into the file, not
                              rebuilt at load time
```

`AfterLoad(sector)` runs as the final step on the read path — a post-processing hook, presumably
rebuilding runtime-only derived structures (adjacency, the live A* graph) from what was just
deserialized, before the sector is marked ready (`this[0x5e] = 0`).

A navmesh sector therefore contains a triangle mesh (`CNavMeshNode`, one per triangle) with its
adjacency, quantized vertex positions, two flavors of AI cover point, dynamic obstacles, and a baked
spatial index — each with a named class and a known position in the byte stream.

## Measured per-sector header

The field order above comes from the serializer; these offsets come from reading real
`nv_<id>.nvm` files (`w1_c_2`, sectors 2576 and 2577):

```
+0x00  u32   0
+0x04  u32   0x4E764D68        the per-sector tag
+0x08  u32   0x00014100        format version
+0x0C  u32   reload size       varies per sector (60300, 38220)
+0x10  u16   unidentified
+0x12  u16   sector id         2576 / 2577 - the global id, matching the file name
+0x14  f32   minX              1024 / 1088
+0x18  f32   minY              2048 / 2048
+0x1C  f32   maxX              1088 / 1152
+0x20  f32   maxY              2112 / 2112
```

The bounding box is 2D — four floats, no Z — and matches the sector's own 64×64 footprint exactly.
That matters for reading the geometry: every vec3 past `SetPackedVectorSettings` is quantised as
three `int16` **relative to this box**, so these four floats are the dequantisation basis.

Sector files are sizeable — around 70–90 KB each, 254 of them in `w1_c_2` — so a sector's navmesh is
far richer than its terrain.

## Dequantising a position

`SetPackedVectorSettings(centre, span)` (`0x09a0a750`) takes the bounding box centre and
`2 × round(max(width, height))`, which must be a power of two, and derives the scale as
`1 << (15 - log2(span))`. A 64 m sector therefore gives `span = 128` and a scale of 256, so:

```
x = centre.x + int16 / 256        centre = the bbox midpoint
y = centre.y + int16 / 256
z =            int16 / 32         Z is always 1/32 m and never uses the box
```

X and Y resolve to about 4 mm and Z to about 3 cm. The scale is per sector, so a position can only be
decoded alongside the header it came from — there is no world-wide constant.

## `CNavMeshNode` — 48 bytes on disk

`CNavMeshNode::SerializeData` (`0x09a11e50`) writes 48 bytes for a current-version archive, against
the 60 the class occupies in memory. Each node is one triangle of the mesh:

```
+0x00  u32    60 - the object's size, discarded on read
+0x04  u16    sector id - the file's own
+0x06  u16    node index - its position in the array
+0x08  u16    1, or a number unique to the node (meaning unknown)
+0x0A  u8     link count - 3 for almost every node, up to 16 seen
+0x0B  u32    link offset - index of the node's first entry in the link block
+0x0F  int16 x, y, z  packed - the triangle's centroid
+0x15  s8 ×3  normal x, y, z, /127
+0x18  u8     flag (version >= 0x13900, meaning unknown)
+0x19  u8     flag (version >= 0x13900, meaning unknown)
+0x1A  u32 ×3 vertex indices into the vertex array after the node array
+0x26  10 bytes, unknown
```

The three normal bytes are what the engine dots against world up and compares to
`NavMeshUtils::COS_ANGLE_SLOPE_LIMIT` to set the node's "too steep" bit, so slope is readable
straight from the file without touching the terrain.

### Links

A node's neighbours are the `link count` entries of the link block starting at `link offset`. Each
entry is `(neighbour node index << 16) | neighbour sector id`, and `0xFFFEFFFF` means nothing is on
the other side. The first three entries belong to the triangle's edges in order: entry k is the
neighbour across the edge from corner k to corner k + 1, and an empty one marks where the walkable
area ends. Any entries past the third are extra neighbours: T-junctions, where a longer edge meets
two shorter ones, and triangles across a sector boundary.

A neighbour in another sector is addressed by that sector's id and an index into its own node array,
so the mesh is continuous across sector files. Entries past a node's link count belong to something
else and must not be read as its links.

Older versions are shorter: below `0x13800` the position is a full `f32vec3` rather than packed,
below `0x13701` there is an extra byte, and below `0x13900` the two flag bytes are absent.

:::note[Verified]
Measured on retail files, against the read path of `CNavMeshNode::SerializeData`:

- In every sector checked, each node's position is the centroid of the three vertices it indexes.
  The check covered 774 nodes in `w1_b_2` sector 4979, and every node of `w1_c_2` sectors 2576 and
  2577.
- Every vertex index is in range, across all 328,386 nodes of `w1_b_2`.
- Link entry k names the triangle that shares edge k in every same-sector case except T-junctions.
- Of 92 cross-sector links out of sector 4979, 90 share an exact world-space edge with their target.
  The other 2 are T-junction pieces of one edge.
- World1 totals 2,810,555 triangles and 3,243,697 vertices over 2,560 sector files. 349 of those
  files are empty, and the mesh has 3,712,778 neighbouring pairs.

Implemented in `JackAll.Tools/World/WorldNavMesh.cs`.
:::

## Unknowns

- The semantic meaning of the level-file header fields (`+0x54` through `+0x70`) and the sector-content
  scalar fields (`+0x5c`, `+0x64`, `+0x6c`, `+0x70`/`+0x74`) — only their storage location and
  read/write order are confirmed, not what they represent.
- The byte layout inside `CNavCover`, `CDynamicNavCover`, `CNavMeshObstacle`, and `CNavMeshQTree` —
  each has its own `SerializeData`, none decoded. `CNavMeshNode` is decoded (above) except its two
  flag bytes, the id word at `+0x08` and the trailing 10 bytes.
- The purpose of the second quantized-vertex array, and the relationship between the inline
  `CNavMeshQTreeWriter`-built tree and the second `CNavMeshQTree` serialized unconditionally at the end
  of `SerializeDataContent` — possibly one is a full-precision editor-time tree and the other a
  runtime-optimized rebuild, not confirmed.
- What exactly selects `modeFlag` 0/2 vs. 3 (single-file vs. per-sector-satellite-files) — whether it's
  a global setting, a per-level authoring choice, or tied to the format version the same way the
  header-length gate is.
- Whether `nv\nv.nvm` (the writer's "same index twice" `MakeSectorFileName(true)` branch) is ever
  actually reached in practice, or is dead/legacy code — no caller using that branch is known.
