---
sidebar_position: 11
---

# `.nvm` — Navigation Mesh

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server`, both the write and the read side. Every byte of a
retail sector file is accounted for: the header, the sector scalars, nodes, vertices, cover points,
cover vertices and the quadtree. Reading all 512 sectors of `w1_c_2` and `w2_b_3` with this layout
leaves no byte over. JackAll reads and writes the format. See also
[the file manifest](../modding/file-manifest.md#6-navigation-mesh-nvm--locked).
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
places actors. `FarCry2_server` carries the generator (`CNavmeshGenerator`, `CNavMeshSectorWriter`,
`CNavMeshWriter::ComputeSectorRadii`, `BuildNavMeshLevel0`). Retail `Dunia.dll` shows only
`CNavMeshGenComponent` of it by name. There is no `FCE_Nav*` export.

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

The mode flag reads `2` (single-file) rather than `3` (per-sector satellites) in every shipped level
file, and the descriptor table is **entirely zero** in all of them, including `world1`'s. The level
file is a grid and extent declaration. The satellites reach the game another way.

## How the game finds a sector file

**(RE-verified)** A sector's navmesh is streamed with the rest of the sector, as a
`CNavMeshSectorResource`. `CWorldSector::MakeRuntimeDataRes` (`0x095c9f80`) adds it to the sector's
resource container through `CNavMeshSector::BuildRsrcDep` (`0x09a1ce20`), which names it
`<level>\generated\nv\sectors\nv_<sectorId>.nvm`. It does so only when bit 0 of the sector's
`SWorldSectorDep` flags is set.

Those flags come from `<world>.sectorsdep.fcb`. `LoadDep` (`0x09c26f70`) reads each `CWorldSector`
entry's booleans into one flag byte:

| Bit | Field | `world1` | `tmpla` |
|---|---|---|---|
| 0 | `HasNavMesh` | true | absent |
| 1 | `HasDescriptor` | true | true |
| 2 | `HasMainSectorData` | true | absent |
| 3 | `HasLandmarkNear` | true | absent |
| 4 | the next boolean, hash `F19B593D` | not seen | absent |
| 5 | `isSectorAccessible`, true when absent | true | true |

**A world ships navmesh for a sector by setting `HasNavMesh` on that sector's `sectorsdep` entry.**
Without it, a `nv_<id>.nvm` on disk is never opened. This is why the editor's worlds never load one:
`CFCXEditorDocument::ExportSectorDependencies` writes only `HasDescriptor`, `HasMainSectorData` and
`DetailMask`.

When the resource arrives, `CNavMeshSectorResource::ClientProcessRawData` (`0x09abf710`) hands it to
`CNavMeshLevel::RequestSectorLoad` with a `NavMeshSectorID` of
`(sectorId % gridWidth) << 7 | (sectorId / gridWidth)`. The grid width is the level file's.

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

## The sector file, byte by byte

**(RE-verified)** from both directions of `SerializeDataHeader` (`0x09a21710`) and
`SerializeDataContent` (`0x09a1e780`); **(measured)** on every sector of `w1_c_2` and `w2_b_3`, each of
which this layout consumes to the last byte. Everything is little-endian, and the version is
`0x14100` throughout retail.

```
+0x00  u32   0
+0x04  u32   0x4E764D68        "hMvN", the tag
+0x08  u32   0x00014100        format version; a sector whose version differs fails to load
+0x0C  u32   reload size       120 + 60 * nodeCount (GetReloadSize: the in-memory sizes)
+0x10  u16   NavMeshSectorID   (x << 7) | y, the sector's column and row in the grid
+0x12  u16   sector id         the global id, matching the file name
+0x14  f32   minX, minY, maxX, maxY   the sector's 2D footprint
+0x24  f32   water level       -1e6 when the sector has no water
+0x28  u64   a world-wide constant
+0x30  u16   0x7F7F in every retail sector
+0x32  u32   link count, then u32[] links
       u32   node count, then 48-byte nodes
       u32   vertex count, then packed vertices, 6 bytes each
       u32   cover count, then 6-byte covers
       u32   dynamic cover count, then 14-byte dynamic covers
       u32   cover vertex count, then packed cover vertices, 6 bytes each
       the quadtree
```

The top two bits of the `NavMeshSectorID` word are whatever was in a register when it was built,
so retail files differ there for no reason: `ClientProcessRawData` builds its id the same way,
`unaffected EDI & 0xFFFFC000` included. Whether the stored word is compared with that id on load was
not traced.

The water level feeds `CNavMeshNode::ComputeIsUnderWater`: a node is under water when all three
corners sit at or below the level minus 0.6 m. Retail uses −1e6 or a real height, such as 16.0
and 42.0.

The 64-bit constant at `+0x28` is the same in every sector of a world: `0x00051162F1B64C58` in
world 1 and `0x001223D31752AD86` in world 2. On write it falls back to `CNavmeshEdition`'s own
`+0x54`/`+0x58` when unset, so it is something the generator stamps, plausibly a build id. Nothing
on the read path was found to use it.

The bounding box is the dequantisation basis for every packed vector after it: see
[dequantising a position](#dequantising-a-position).

### Covers

**(RE-verified)** `CNavCover::SerializeData` (`0x099fc080`) writes 6 bytes:

```
+0  u8   height, in 8 mm steps (height * 125, capped at 250)
+1  u16  index into the cover vertex array
+3  u8   type bits; retail uses 0x00, 0x10, 0x20, 0x30
+4  u16  node index
```

A cover has no position of its own. `CNavMeshSector::AfterLoad` (`0x09a1cb50`) points each one at
the cover vertex array, which is what the second packed array is, and gives it an id from the
sector and its index. `CDynamicNavCover::SerializeData` (`0x099fc700`) writes the same 6 bytes, then
two `u32`s. Retail has no dynamic covers. **(measured)** Of 25,660 covers in `w1_c_2` and `w2_b_3`,
175 have a cover vertex index and 30 a node index past the end of their sector's array.

### The quadtree

**(RE-verified)** The tree's shape does not depend on the mesh.
`CNavMeshQTreeWriter::BuildQTreeStructure` (`0x09abb780`) splits a quad while its half-width is at
least 4 m, so a 64 m sector always has 341 quads (1 + 4 + 16 + 64 + 256) with 4 m leaves. The four
children are allocated together, in the order +X+Y, −X+Y, +X−Y, −X−Y, and are then built in that
order depth-first. `Append` files each node by its centroid's X and Y.

On disk, `CNavMeshQTree::SerializeData` (`0x09abed10`) reads:

```
u32  quad count
per quad:
  u32  1 if the quad has children
  f32  4 bounds
  u16  node count, u16 first index into the node list
  u16  4 child indices, only when it has children
u32  node list length, then u16[] node indices
```

### `AfterLoad`

`AfterLoad` sets each node's parent and gives each cover its id and the cover vertices. Nothing
else is rebuilt, so links, graph ids and clearance radii must all be correct in the file.

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
+0x08  u16    graph id - the connected region the node belongs to
+0x0A  u8     link count - 3 for almost every node, up to 16 seen
+0x0B  u32    link offset - index of the node's first entry in the link block
+0x0F  int16 x, y, z  packed - the triangle's centroid
+0x15  s8 ×3  normal x, y, z, /127
+0x18  u8     1 when the node is under water
+0x19  u8     1 when the node stands on an object rather than the terrain
+0x1A  u32 ×3 vertex indices into the vertex array after the node array
+0x26  u8 ×10 clearance radii, in 4 cm steps
```

The three normal bytes are what the engine dots against world up and compares to
`NavMeshUtils::COS_ANGLE_SLOPE_LIMIT` to set the node's "too steep" bit, so slope is readable
straight from the file without touching the terrain.

**Graph id.** `CNavMeshSectorWriter::SetGraphIdToNodeAndFriends` (`0x09a1d170`) floods one id across
linked nodes that share the under-water bit. It can require a 0.46 m agent radius to fit through
the shared edge. `0xFFFF` means none was assigned. **(measured)** In `w1_c_2` and `w2_b_3`, 28% of
nodes are in graph 1.

**Flags.** The two flag bytes are bits 0 and 6 of the node's travel-restriction byte, which
`CNavMeshNode::SatisfiesTravelRestrictions` (`0x09a10ec0`) tests against an agent's allowed
terrain. Bit 3, too steep, is recomputed from the normal on load. **(measured)** In `w1_c_2`,
19,291 of the 19,304 nodes that `ComputeIsUnderWater` puts under water carry `+0x18`, and no other
node does. `+0x19` matches `ComputeIsAboveTerrain`'s name and is set on 16,267 dry nodes.

**Clearance radii.** `CNavMeshWriter::ComputeSectorRadii` (`0x09a25140`) samples ten points of the
triangle and stores the largest circle that fits at each, as `min(radius, 10 m) × 25`. The points are:

| Slot | Point |
|---|---|
| 0 | the centroid |
| 1, 2, 3 | corners 0, 1, 2 |
| 4, 5, 6 | the midpoints of edges 0–1, 0–2, 1–2 |
| 7, 8, 9 | the midpoints between those midpoints: (4, 5), (4, 6), (5, 6) |

The radius comes from `CNavMeshNode::FindMaximumFittingRadius` (`0x09a14570`). It walks outward
across links, at most 10 m, and measures the 3D distance to the nearest edge that blocks: an edge
with no link, or a link to a node the agent's travel restrictions forbid.
`CNavMeshNode::FastRadiusCheck` (`0x09a10f20`) compares an agent's radius against one slot, and
`IdentifyPositionIndexes` (`0x09a14310`) picks the slots for the edge an agent crosses. **A navmesh
with zero radii lets no agent through.**

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

## Generating a navmesh

JackAll's `navmesh build` writes sectors for a level of your own
([command line](/jackall/cli-formats)). It reads the terrain, cuts out the footprint of
every placed entity whose descriptor has a `StaticPhysComponent` or `CompoundPhysComponent`, and
fills in what the engine's generator would: links, graph ids, the clearance radii and the quadtree.
Then it sets `HasNavMesh` in the world's `sectorsdep.fcb`.

**(measured)** Run over `w1_c_2` with world 1's library:

- Retail covers 54% of the level's ground and the generator 61%.
- Where retail covers a sector almost fully, the generator keeps 87.5% of that area. It cuts out
  more around props.
- 8.6% of what the generator keeps is steeper than retail allows, because it never drops a slope.
- It makes 2.35 times as many triangles, because it splits cells down to 1 m to follow hills.

It was built for flat ground like `tmpla`'s. For real relief it needs slope rejection and fewer,
larger triangles, which is what Recast does. Nothing it wrote has been loaded in the game yet.

## Unknowns

- The meaning of the level-file header fields (`+0x54` through `+0x70`), and of the sector's 64-bit
  constant at `+0x28` and its `0x7F7F` at `+0x30`.
- The meaning of a cover's type bits, and how a cover's height is measured.
- How the graph ids of different sectors are kept apart, and whether the AI refuses a path between
  two nodes whose graph ids differ.
- What selects `modeFlag` 0/2 vs. 3. Retail ships 2 and streams the sectors as resources instead
  ([how the game finds a sector file](#how-the-game-finds-a-sector-file)).
- Whether `nv\nv.nvm` (the writer's "same index twice" `MakeSectorFileName(true)` branch) is ever
  actually reached in practice, or is dead/legacy code — no caller using that branch is known.
