---
sidebar_position: 11
---

# `.nvm` — Navigation Mesh

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server`, both the write and the read side. Every byte of a
retail sector file is accounted for: the header, the sector scalars, nodes, vertices, cover points,
cover vertices and the quadtree. Reading all 512 sectors of `w1_c_2` and `w2_b_3` with this layout
leaves no byte over. JackAll reads and writes the format. See also
[the file manifest](../modding/file-manifest.md#6-navigation-mesh-nvm--tooled-jackall).
:::

**(RE-verified)** The engine's generator is not Recast, though a leaked build-tool plugin list
names a `RecastNavmeshCompiler`. `FarCry2_server` carries Ubisoft's own **PopNavmesh**, driven by
`CNavmeshGenerator::Execute` (`0x09a5a8e0`). It works on geometry, never on voxels:

1. It takes the terrain at every whole metre, and the collision of physics entities.
2. It gathers the triangles no steeper than 45° into "platforms".
3. It cuts holes where an obstacle's triangles cross a platform's.
4. It triangulates each platform in 2D with Shewchuk's Triangle, clipped at sector borders.
5. It links the patches where their open edges coincide.

[Generating a navmesh](#generating-a-navmesh) covers the stages and what JackAll does instead.

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
| 4 | `HasLandmarkFar`, hash `F19B593D` | not seen | absent |
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

**(measured)** Under water, retail does not mesh the bed. All 19,291 under-water nodes of `w1_c_2`
lie on one flat floor exactly 0.62 m under their sector's water surface, whatever the terrain
does beneath. They are what boats path on.

**(RE-verified)** The 64-bit value at `+0x28` is `TimeGenerated`, a high-performance timer reading
taken when the navmesh was generated. It is the same in every sector of a world:
`0x00051162F1B64C58` in world 1 and `0x001223D31752AD86` in world 2. Nothing reads it.

The bounding box is the dequantisation basis for every packed vector after it: see
[dequantising a position](#dequantising-a-position).

### Covers

**(RE-verified)** `CNavCover::SerializeData` (`0x099fc080`) writes 6 bytes:

```
+0  u8   height, in 8 mm steps (height * 125, capped at 250)
+1  u16  graph id of the node an agent takes the cover from
+3  u8   type bits: 0x20 the start corner is exposed, 0x10 the end
+4  u16  index into the cover vertex array: the cover's start, its end next
```

A cover is a stretch of obstacle edge, walkable side on its left from start to end. It has no
position beyond its two cover vertices. `CNavMeshSector::AfterLoad` (`0x09a1cb50`) points each one
at the cover vertex array, which is what the second packed array is, and gives it an id from the
sector and its index. `CDynamicNavCover::SerializeData` (`0x099fc700`) writes the same 6 bytes, then
two `u32`s. Retail has no dynamic covers.

**(measured)** In all 16,558 covers of `w1_c_2`:

- `+4` is inside its sector's cover vertex array.
- `+1` is a graph id one of the sector's own nodes carries.
- `fcxNavCoverManager::DealWithRequest` (`0x0863a360`) compares that graph id with the requester's
  node.

#### How the generator lays them

**(RE-verified)** `CCoverGenerator::GenerateCovers` (`0x09ace9d0`) walks the outlines of every
sector: the hole each obstacle cuts and the open boundary of the mesh. It treats each segment of
length L in turn.

- **Splitting** (`GenerateCover_Split`, `0x09ace7f0`):
  - up to 2.6 m, one cover;
  - up to 3.9 m, two halves;
  - longer, 1.3 m at each end and the rest between.

  **(measured)** `w1_c_2` is full of 1.3 m pieces.
- **Height** (`GenerateCover`, `0x09acdd80`): a grid of level probes, 20 rows 0.1 m apart and a
  column every 0.1 m. Each probe runs from 0.3 m on the walkable side to 0.6 m into the obstacle.
  - The height is the first row where any probe misses the obstacle, or 2.0 m when none does.
  - Under 0.75 m is no cover.
- **Type bits** (`CalculateCoverEdgeInfo`, `0x09acd690`): an end is exposed, so an agent can lean
  out past it, when the outline turns away from the walkable side there by more than 45°, or does
  not go on.
- **Validity** (`CNavCover::IsCoverLocationValid`, `0x099fccc0`): the stand point is 0.9 m out from
  the middle of the edge, and an agent of radius 0.8 must fit there. `FilterSectorCovers`
  (`0x09a23760`) drops the covers that fail and stores that node's graph id.

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
linked nodes that share the under-water bit. **(RE-verified)** It enters a neighbour only when that
neighbour's clearance at the shared edge is at least 12 (0.48 m). The edge is sampled at its middle
or at either end, the slots `IdentifyPositionIndexes` (`0x09a14310`) picks. `0xFFFF` means none was
assigned. **(measured)** In `w1_c_2` and `w2_b_3`, 28% of nodes are in graph 1, and 25,224 of
`w1_c_2`'s nodes are alone in theirs.

**(RE-verified)** Graph ids are numbered per level, from 1, by `CNavMeshLevelWriter::BuildGraphs`
(`0x09a0cc80`), and the flood crosses sector borders. They matter at run time:
`CPathFinder::StartRequest` (`0x09a3a040`) fails a request at once when its start and goal nodes
have different graph ids on the same map. Ground an agent cannot walk between must therefore never
share an id, and ground it can walk between must.

**(RE-verified)** How a request finds its start and goal: `CPathFinder::FindStartGoalNodes`
(`0x09a39b70`).

- **The node under the agent.** It takes the triangle under the agent's position when
  `CanFitAgentAtPosWithConstraints` (`0x09a15860`) passes there. That test is geometric: it walks
  outward through the links and fails on any blocking edge nearer than the agent's radius.
- **The nearest point that fits.** Otherwise `GetClosestValidNodePos` (`0x09a16940`) and a search of
  the neighbours take the nearest of each triangle's centre, corners and edge middles where the
  slot (`FastRadiusCheck`, `0x09a10f20`) and the walk both pass.
- **The radius.** Pawns ask with 0.46 m, which the `CPathFinderContext` constructor (`0x09a2ba10`)
  sets, the same as the generator's flood.

So a node an agent fits inside, with no edge it fits through, has a graph id of its own, and a path
asked from it fails. **(measured)** Retail has 160 such nodes in `w1_c_2`.

A cover's graph id is compared by `fcxNavCoverManager::DealWithRequest` (`0x0863a360`). A pier
anchor's is compared by `CPierAnchor::GetGraphID` (`0x08617d20`). Several tasks compare graph ids
too, among them `CTaskFindRandomDest::FindBuddyDown` (`0x0832d840`) and
`CTaskHighTargetAttackPos::CheckAttackPosValidity` (`0x08347790`).

**Flags.** The two flag bytes are bits 0 and 6 of the node's travel-restriction byte, which
`CNavMeshNode::SatisfiesTravelRestrictions` (`0x09a10ec0`) tests against an agent's allowed
terrain. **(measured)** In `w1_c_2`, 19,291 of the 19,304 nodes that `ComputeIsUnderWater` puts
under water carry `+0x18`, and no other node does. `+0x19` is set by `ComputeIsAboveTerrain`
(`0x09a11cc0`): a dry node any of whose corners is more than 0.001 m over the terrain. It is set
on 16,267 dry nodes.

**(RE-verified)** The travel-restriction byte, against the agent flags each kind of agent passes:

| Bit | Node | Where it comes from | Agent flag the node needs |
|---|---|---|---|
| `0x01` | under water | file `+0x18` | `0x08`, and then nothing else is checked |
| `0x02` | disabled by a volume | `CAIToggleNavmeshComponent`, at run time | `0x10` |
| `0x04` | restricted by a volume | the same | `0x02` |
| `0x08` | too steep: the normal more than 30° off vertical (`COS_ANGLE_SLOPE_LIMIT`) | recomputed on load | `0x04` |
| `0x40` | above terrain | file `+0x19` | never tested |

A dry node needs agent flag `0x01`. Pawns pass `0x07`, so they walk steep nodes. Animals pass
`0x03`, so steep nodes stop only them. Wheeled vehicles pass `0x05` and boats `0x08`: boats use
under-water nodes only.

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
with no link, or a link to a node the agent's travel restrictions forbid. **(RE-verified)**
`ComputeSectorRadii` measures dry nodes as a pawn (agent flags `0x07`) and under-water nodes as a
boat (`0x08`), so the edge between dry ground and water blocks from both sides.
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

### What the engine's generator does

**(RE-verified)** in `FarCry2_server`, where `CNavmeshGenerator::Execute` (`0x09a5a8e0`) runs the stage
table at `0x0a3e5100`:

- **Terrain.** `CNavmeshEdition::GetTerrainTesselation` (`0x09aca3a0`) takes the heightfield at every
  whole metre. Each quad is split into two triangles, always along the same diagonal: `(x+1, y)` to
  `(x, y+1)`. The terrain is cut into chunks of the sector size, 64 m.
- **Objects.** `CollectObstaclesGeometry` (`0x09ac8250`) takes physics entities, not render meshes.
  The editor decides which of their Havok shapes to hand over, so that part is not in this binary.
  An obstacle whose foot is 0 to 0.15 m off the ground is pushed down into it
  (`ExtrudeToCrossGround`), so it cuts the terrain.
- **Platforms.** A triangle is walkable when its normal is within 45° of vertical
  (`ExtractMeshShapePlatforms`, `0x09ab7ec0`). Connected walkable triangles form a platform. A
  platform under 2 m² is dropped.
- **Holes.** Where an obstacle's triangles cross a platform's, the crossing lines become holes. The
  mesh runs right up to the obstacle: agent size is left to the clearance radii.
- **Triangles.** Each platform is triangulated in 2D by Shewchuk's Triangle, with a minimum angle of
  20°, then clipped at sector borders. The heights are restored afterwards.
- **Links.** Patches are linked where their open edges coincide within 0.1 m in plan. Those links
  are the extra entries past a node's third.
- **Water.** Terrain far enough under the water is removed (`RemoveUnderWaterGeometry`).
- **Covers.** [Covers](#how-the-generator-lays-them) are laid along the hole outlines and open
  edges.
- **Unused areas.** `FilterUnusedAreas` (`0x09a596a0`) deletes every graph that holds no placed AI
  agent. That is why retail covers only about half of a level's ground: mountains and the tops of
  rocks are gone.

### JackAll's generator

JackAll's `navmesh build` ([command line](/jackall/cli-formats)), and `world pack` for a new world,
write every sector of a level. They take the same inputs as the engine's generator:

- the terrain, every whole metre, cut along the same diagonal, and raised under water to the floor
  retail lays there;
- the collision of every placed entity with a static, compound or rigid physics component. A body
  with a [`NAV_` twin](./hkx.md#what-the-files-hold) uses the twin. A breakable object contributes
  only its intact state's bodies;
- the trunks of the trees in the landmark files' vegetation.

The surface comes from **Recast**, through DotRecast. Recast voxelises rather than cutting holes,
which copes with any terrain and any overlap of shapes:

1. **Recast tiles.** Each sector is one Recast tile, built from 0.25 m cells. The rules: no slope
   over 45°, no step over 0.3 m, headroom of 1.6 m. The ground counts as solid below its surface,
   and nothing is shrunk away from walls. When Recast's watershed regions fail on a tile, which
   DotRecast does where C++ Recast would only log, the tile is partitioned monotonely instead.
2. **Corners snapped.** Every polygon corner is moved from Recast's voxel top down onto the real
   surface it stands for, the terrain or a walkable face of an object.
3. **Tiles joined.** Corners on one 1/256 m grid point and within a climb in height are welded.
   Where two sectors' corners along their border differ, each splits the other's edges, so every
   edge meets exactly one other or none.
4. **Unreached ground dropped.** A region kept must touch the terrain and be at least 200 m², or
   hold a placed soldier, animal, vehicle or smart-terrain point. This is the engine's unused-area
   filter, kept looser so ground an agent may be spawned on at run time stays.
5. **Refined.** A triangle is split at the middle of its longest side when it is longer than 12 m,
   or strays more than 0.5 m from the surface where it is sampled. Everything sharing that side is
   split with it.
6. **Flipped.** A pair of triangles is flipped to the better-shaped diagonal (the Delaunay test, in
   plan) when the flipped pair still follows the surface.
7. **Filled in.** Then come the links, the per-level graph ids, the clearance radii, the
   under-water and above-terrain flags, the covers and the quadtree, as the engine's writer makes
   them.

**(measured)** Run over `w1_c_2` with world 1's library, in about 25 s:

| | Retail | JackAll |
|---|---|---|
| Ground covered | 54% | 72% |
| Of retail's walkable cells, covered at the same height | | 94.7% |
| Triangles | 344,947 | 634,261 |
| Triangles steeper than 45° | under 1% | 3%, slivers holding 0.3% of the area |
| Covers | 16,558 | 16,615 |
| Retail's covers with one of JackAll's within 1.5 m | | 81% |

Every link has its way back and meets its neighbour edge for edge. 293 of 430,000 open edges lie
along another without a link, almost all of them 0.25 m steps of the voxel outline.

The extra ground is mostly what the engine's unused-area filter deleted: plateaus, rock tops a
stair reaches, and mountainsides gentle enough to walk.

**(proven)** In `testrange`, a flat world built from `tmpla` with an earlier, simpler generator,
soldiers spawned at run time move and shoot, and the game keeps running. Nothing built by this
generator has been seen in game.

## Unknowns

- The level-file header fields `+0x54` through `+0x70`. The writer fills them with the level's box
  and its sector grid, but how those land at file offsets is inferred. The sector's `0x7F7F` at
  `+0x30` is a field the writer never sets.
- Which Havok shapes the editor's callback hands the engine's generator, and whether it prefers a
  body's `NAV_` twin.
- What selects `modeFlag` 0/2 vs. 3. Retail ships 2 and streams the sectors as resources instead
  ([how the game finds a sector file](#how-the-game-finds-a-sector-file)).
- Whether `nv\nv.nvm` (the writer's "same index twice" `MakeSectorFileName(true)` branch) is ever
  actually reached in practice, or is dead/legacy code — no caller using that branch is known.
