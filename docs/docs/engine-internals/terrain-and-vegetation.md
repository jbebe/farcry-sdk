---
sidebar_position: 13
---

# Terrain, Sectors and Vegetation — the runtime model

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`FarCry2_server`** (the Linux dedicated-server ELF). Its symbol
table keeps real class and method names — `CTerrain`, `CSector`, `CFCXEditorCollectionManager`,
`CEditorCollectionComponent` — which `Dunia.dll` does not. This page covers how the terrain and
vegetation systems are shaped in memory and how edits flow through them; the on-disk byte layout of
a sector lives in [`.sdat`](../file-formats/sdat.md).
:::

## The terrain grid

`CTerrain` is a singleton (`CTerrain::ms_instance`) holding a flat array of `CSector*`. Three statics
define the world size, and every accessor recomputes the same address arithmetic from them:

```
sector    = Sectors[(y >> 6) * SectorCountX + (x >> 6)]
cellIndex = (y - sector->OriginY) * 0x41 + (x - sector->OriginX)
height    = sector->PackedData->Cells[cellIndex].Height * 0.0078125
```

- `m_iTerrainSideLengthX` / `m_iTerrainSideLengthY` — the world in cells, `0x201` (513) in shipped
  maps.
- `m_iSectorSideCountX` — sectors per row, 8.
- Sector origins are `sectorX << 6` / `sectorY << 6`, so each sector owns 64 cells, and `64 * 8 + 1
  = 513`.

Each sector stores **65×65 cells**, not 64×64: the 65th row and column duplicate the first row and
column of the neighbouring sector. That shared edge is why the range checks all contain a
`x == m_iTerrainSideLengthX` clamp, and why writes fan out to more than one sector (below).

The height scale is **1/128** (`0.0078125`), identical in `CTerrain::GetZ`,
`CTerrain::GetSectorHeightFloat`, `CSector::GetZApr` and `CSector::ComputeMinMaxZ`. A `u16` sample
therefore covers 0–512 metres.

`CTerrain::GetZ` returns `-1.0` for an out-of-range cell or a sector whose packed data is not
resident; `GetSectorHeightFloat` returns `0.0` in the same situation. Callers that need to
distinguish "no terrain" from "sea level" have to pick the right one.

## `CSector`

A `CSector` is a thin handle, not the terrain data itself. It carries the sector's identity and
water state, and points at two things that hold the actual geometry:

- **`PackedData`** — the 0x595c-byte block of cells, normals and the surface-type palette. This is
  the block that gets serialized to [`.sdat`](../file-formats/sdat.md).
- **`SceneSectorHandle`** — a `CSceneObjectHandle` into `CSceneObjectContainer<CSceneTerrainSector>`,
  which owns the renderable side (bounding box, visibility, mask-dirty counter, water plane).

Two flags gate most access. `HasPackedData` false means the sector is a stub — `GetZ` returns `-1`
and `GetNormal` returns `(0, 0, 1)`. `Loaded` (`CSector::IsLoaded`) tracks resource residency
separately.

Water is per sector: a raw `WaterLevel` float plus two independent env flags. `GetWaterLevel`
returns `-1e6` unless at least one flag is set, and `SetWaterLevel(-1e6)` is the documented way to
delete a sector's water plane rather than a sentinel that happens to work.

`ComputeMinMaxZ` scans all 4225 cells and forces the span to at least 1 metre when the sector is
flat, so a perfectly level sector still gets a non-degenerate bounding volume.

## The height write path

`CTerrain::SetSectorHeightFixed` is the only route by which heights change, and it does three
things that any offline editor has to reproduce:

1. **It writes the same height into up to three sectors.** When `x % 64 == 0` it also updates the
   sector to the left, and when `y % 64 == 0` the sector above, because those sectors hold their own
   copy of the shared edge. Skipping this leaves visible seams at sector boundaries.
2. **It pulls a writable copy of the packed data** through the scene object container
   (`ModifyOriginal`) and re-points `CSector::PackedData` at it. The pointer is not stable across
   writes — caching it across an edit is a bug.
3. **It marks both levels dirty** — `CSector::Dirty` and `CTerrain::PackedDataDirty`.

`CTerrain::UpdateSectorPackedData` consumes that second flag. `CTerrain` keeps a parallel array,
`SectorPackedDataTable`, of raw packed-data pointers indexed the same way as `Sectors`; it exists so
hot paths can skip the `CSector` indirection, and this function resyncs it and clears the flag.

## Surface types and holes

The per-cell `Flags` byte's **top three bits** are a detail-layer index, 0–6. The value **7 is
reserved to mean hole** — `CSector::GetHole` and `CTerrain::IsSectorEditorHole` both test
`Flags >> 5 == 7`.

For anything that is not a hole, that index selects one of **seven bytes in a surface-type palette**
stored inside the same packed block. `CSector::GetSurfaceType` returns `0xff` for a hole;
`CTerrain::GetSurfaceType` maps that to `0`, so a caller reading through `CTerrain` cannot tell a
hole from surface type 0.

## Normals

Terrain normals are stored as two bytes per cell in two separate planes, and Z is reconstructed
rather than stored — `nz = 1 - sqrt(nx*nx + ny*ny)`. That is not a renormalisation: the result is
not a unit vector and the engine uses it as is, so anything recomputing normals for FC2 terrain
should match this rather than emit a correct one. `CSector::GetNormal` returns `(0, 0, 1)` for a
sector with no packed data resident. See [`.sdat`](../file-formats/sdat.md#normals) for the encoding
and plane offsets.

## Terrain brushes

The `FCE_Terrain_*` family (see [the editor API surface](./editor-api-surface.md)) all share one
shape: `(fX, fY, fStrength, CBaseGrid *pBrush)`, with `Smooth` dropping strength and `Terrace`
adding two extra floats. The brush is a `CSimpleGrid` of floats built by `FCE_Brush_Create`, whose
width and height define the footprint.

Every operation writes its arguments into a **shared per-filter singleton** at fixed globals — brush
pointer, then X, then Y at consecutive slots — and then runs the kernel over the footprint centred
on `(fX, fY)`, clamped to the `0 … 0x201` cell range. Operations come in `Begin` / apply / `End`
triples; the `_End` call commits.

`FCE_Terrain_Grab_Begin` is the most involved: it snapshots the heightmap under the footprint,
removes texture projections over the rect, clamps every sample to 0–255, and records the minimum
height for the drag. It also starts object tracking when
`CFCXEditorSettings::SnapObjectsToTerrain` is set, so placed objects follow the deformation.

## Collections — the vegetation layer

Scattered vegetation is a "collection", and the editor-side owner is
`CFCXEditorCollectionManager`. Two facts constrain everything built on it:

- **There are exactly eight collection slots.** `CollectionEntries` and `CollectionSeeds` are both
  eight-element arrays.
- **The paint mask stores one byte per terrain cell** naming the slot that paints it, and the value
  **8 is the "no collection" sentinel** written by both `ClearMask` and `ClearMaskId`. So a valid
  slot is 0–7 and 8 means empty.

`AssignCollectionId` binds an inventory entry to a slot and reseeds its scatter: from the supplied
seed it runs the classic `0x343fd` / `0x269ec3` LCG to fill 64 `u32` of a per-slot random table,
then refetches resources and regenerates the seeding mask. **Placement is therefore fully
determined by that seed** — but the `FCE_` wrapper draws the seed from `ndRandU32`, so re-assigning
the same entry through the editor API reshuffles all of its vegetation rather than leaving it put.

`UpdateCollections` takes an origin plus a width and height, converts them to a rect internally, and
updates the terrain surface alongside the collections — the two are refreshed together, never
independently.

## Vegetation zones

Zones are a separate, per-component concept living on `CEditorCollectionComponent`: a
`std::map<unsigned long long, StVegetationZoneInfo>` keyed by a 64-bit zone id, where the value is
just a `CryVector` of the `CClusterComponent*` spawned for that zone. `AddVegetationZone` is
get-or-insert; `RemoveVegetationZone` destroys the zone's entities first, then erases the node and
refreshes the component.

The serialized form, `StSerialVegetationZoneData`, is a zone id plus **nine parallel `CryVector`s** —
resources, per-resource cluster counts, bounding volumes, per-cluster instance counts, XY positions,
Z positions, Z orientation data, colours, and a list of cluster records. Splitting instance data
across parallel arrays rather than an array of structs is consistent throughout.

## Retail campaign vegetation lives in the landmark files

The collection system above is the **editor's** authoring mechanism. A shipped campaign level does not
store its vegetation the same way: placement is in
`levels/<cell>/generated/worldsectors/landmarkfar_<sectorId>.data.fcb`, keyed by **global** sector id.
Each holds one `WorldSector` → `MissionLayer` → `Entity` whose `Components` carry a
`CCollectionComponent`:

```
CCollectionComponent
  VegetationData        dataVersion (5), bboxMin, bboxMax
    VegetationZoneData  zoneId, resourceList, resClustersCntList, boundingVolumeList,
                        clustersInstancesCntList, posXYList, posZList, orientZDataList, colorsList
```

Those nine lists are the parallel vectors of `StSerialVegetationZoneData` named above, and
`CCollectionComponent::SpawnVegetationFromSerializationData` is what walks them at load. A sibling
`CCollectionIgnitorComponent` carries an `IgnitorZonesList`, which is what makes vegetation burn.
`landmarknear<id>.data.fcb` is roughly ten times larger and additionally carries `LightVegeGrid`.

### List packing

Every list is a `u32` element count followed by that many 4-byte elements. The element meanings come
from `StSerialVegetationZoneData::RestoreGraphicCluster` and the `CollectionComponentSerialUtils`
helpers beside it:

| List | Element |
|---|---|
| `resourceList` | resource id, one per resource — the **CRC32 of the resource's own path** |
| `resClustersCntList` | how many clusters that resource owns |
| `clustersInstancesCntList` | instance count in the **low byte**, instance-array offset in the **upper 24 bits** |
| `boundingVolumeList` | four floats per cluster — a bounding sphere |
| `posXYList` | two `u16`, each **× 0.1**, giving X and Y in **global world metres** |
| `posZList` | plain `float` world Z |
| `orientZDataList` | two `i16` packed into the `u32` |
| `colorsList` | three `u32` per instance |

The XY packing is the one that resists guessing: the values are decimetres in world space rather than
anything sector-relative, so `0x50342A39` is (1080.9, 2053.2). `GetInstancesCntFromPacked` is
`& 0xff` and `GetInstancesIndOffsetFromPacked` is `>> 8`.

Per-sector vegetation extents are available without walking any of it, from
`VegetationData.bboxMin`/`bboxMax`.

### Resource ids are path hashes, and most of the scatter is grass

:::info[Verified against the retail corpus]
:::

A `resourceList` element is the same CRC32 the `.fat` index keys on, taken over the resource's
normalized path — so a reverse lookup against the known path list resolves it. All 84 distinct ids
across `world1`'s landmark files resolve this way, with no misses.

What they resolve to is worth knowing before building anything on top. `world1` places **2,472,226
instances** across 5,377 landmark files:

| | resources | instances |
|---|---|---|
| grass meshes (`1_grass<biome>_<letter>.xbg`) | 9 | 2,340,025 (95%) |
| all `.xbg` | 24+ | 2,370,625 (96%) |
| `.rtx` [RealTree](../file-formats/rtx.md) | 60 | ~101,000 (4%) |

`1_grasssavannah_a.xbg` alone is placed 793,864 times. Two consequences:

- The collection system is **not** a plant system. It scatters `terrain/rocks/*` and
  `objects/natural/dung*` through the same lists, so a tool that filters it by folder loses geometry.
- Almost all of it is ordinary `.xbg`, so a renderer that can draw a crate can draw the scatter. The
  RealTree share is small — bushes and shrubs, not the bulk. At this scale, nothing on that path can
  afford to allocate per instance.

Sampling is treacherous here: a few hundred landmark files taken in directory order land in desert
sectors, which are rock-heavy and grass-light, and give the opposite ratio. Count the world.

### Where campaign vegetation is not

Checked against `world1` and `world2`:

- **No collection mask file exists.** A level cell ships only `.fcb`, `.xbt`, `.zsr`, `.sdat`, `.srl`
  and one `.xml`. The `ige/collection.mask` written by `SaveMasks` appears only inside
  [`.fc2map`](../file-formats/fc2map.md) documents.
- **Not placed as entities, and no cluster or collection instances in sector data.** Across 40
  `worldsector*.data.fcb` files in `w1_c_2` there are none at all; across 40 in `w2_b_2` there are two
  `CRealtreeComponent`, which are individually placed trees rather than scattered vegetation. Sector
  files in these cells are a few kilobytes, far too small to hold per-instance placement, and 33,000
  sampled entities contain about 16 plant props.
- **Nothing in the sector descriptor.** `sector<id>.desc.fcb` holds `DetailTexMask`, the sector id,
  the neighbour list and landmark/mission references, and no collection fields.
- **Nothing in `mapsdata.fcb`, `managers.fcb` or the entity library** (833 sectors sampled
  world-wide).

What `<world>.managers.fcb` does hold is a definition list: a `Collections` node with 144
`Collection` entries, each carrying a name, an asset GUID and a hash. The names are biome-shaped —
`FCX_SemiDesert01`, `FCX_Desert01`, `FCX_RoadDesert01`, `FCX_EmptyVoid`. The entity library
separately declares `CGrassDisplacementComponent` and `CVegetationSlowdownComponent` on archetypes.

So the 144 collections are a palette, and no per-location assignment selecting among them is present
in the sector data, descriptors or masks. The `.sdat` `EnvSettings` blob, the remaining per-sector
store, is a raw memory snapshot whose varying words look like retained pointers (`0x07xxxxxx`), not
an authored slot table.

## One tuft

:::info[Verified via reverse engineering]
Read from the editor's save path (`StClusterData::StoreGraphicCluster`,
`StSerialVegetationZoneData::Serial_PrepareForSave`) and the load path
(`RestoreGraphicCluster`, `CSceneGraphicObjectClusterHelper::AddInstance`), then checked against
every shipped zone: JackAll reads and rewrites the nine lists of 12,197 of the 12,203 byte for byte.
:::

A placed copy of a mesh is a 32-byte record in memory: its position, its turn, and three colour
words. The lists hold the same fields, split one list per field:

| Field | What it holds |
|---|---|
| `posXYList`, `posZList` | the position: X and Y rounded to the decimetre, Z a float |
| `orientZDataList` | the turn about Z only: `sin × 32767` in the high `i16`, `cos × 32767` in the low. The angle is `atan2(sin, cos)` |
| `colorsList` word 1 | RGB the ground's colour under the tuft, A its height scale (`× 255`). A of 0 is read as `0xFF` |
| `colorsList` word 2 | the ground's normal under the tuft, each axis `× 0.5 + 0.5` in R, G and B, Z as `CSector::GetNormal` makes it ([normals](#normals)). Flat ground is `0x007F7FFF` |
| `colorsList` word 3 | RGB how much sky the tuft sees, near white in the open; what A carries is not established |

The editor groups a resource's tufts into **clusters by the 16 m square they stand in**, about 96
to a square in a shipped savannah field. A cluster is drawn and culled as one; its sphere comes from
`CSceneGraphicObjectClusterHelper::UpdateBoundingVolume`. Each tuft counts as a sphere at its mesh
box's centre whose radius is the box's shortest side, as `GetGeometryLocalBSphere` makes it. The
cluster's sphere sits at the middle of their bounds, and its radius is the longest side of those
bounds, so the sphere is about twice as large as it needs to be. `VegetationData`'s `bboxMin` and
`bboxMax` bound every tuft's turned mesh box in the sector.

A RealTree uses the same lists differently (`StClusterData::AddRealtreeInstance`,
`StSerialVegetationZoneData::RestoreRealtreeCluster`; **RE-verified**):

- its turn is a whole number of hundredths of a degree, not a sine and cosine;
- its first colour word is always `0xFFFFFFFF`, the second holds the ground's colour under it, and
  the third its sky;
- its cluster is saved with an all-zero sphere and the loader never reads one;
- it brings its own collision (`CRealtreeClusterComponent::CreatePhysics`), so no physics flag is
  needed for it in [the mesh's entry](#the-mesh-a-tuft-names).

Shipped levels keep RealTrees in a sector's `landmarkfar` file, filed under the category
`LandmarkFar`, and grass in its `landmarknear` file under `LandmarkNear`. Rocks and other meshes can
be in either file. The two files are shaped alike.

**A cluster holds at most 255 tufts.** Its count is the low byte of its packed word, and the editor
packs `offset << 8 | count` without masking, so a 257th tuft carries into the offset. The engine then
reads a few tufts from one place further on and never draws the rest. Six campaign sectors shipped
that way (`landmarknear2753` and `2836` in `w1_c_3`, `3983`, `4062`, `4063` and `4540` in `w2_b_4`),
each missing most of one cluster's grass.

## The mesh a tuft names

A tuft is spawned only if the world's `CCollectionManager` lists its mesh: the
`SerializationData` → `ResourcesList` → `Resource` entries of `<world>.managers.fcb`
(`CCollectionManager::GetSafeResourceInfo`; **RE-verified**). Each `StClusterResourceInfo` carries:

| Field | Meaning |
|---|---|
| `classId` | 1 a mesh (`CGraphicClusterComponent`), 2 a RealTree (`CRealtreeClusterComponent`) |
| `fileNameId` | the mesh, by path hash |
| `flags` | bit 0 draws the tuft in its own colour word, bit 2 gives it physics; `RefreshHasGrass` sets `0x80` when the mesh has a material named `Grass` |
| `playerCoverage` | 1 for a mesh tall enough to hide a crouched player from the AI ([grass and stealth](./ai.md#grass-and-stealth)), 0 for the rest |
| `physPathId` | the physics a mesh with bit 2 collides with |

`world1`, `world2`, `tmpla`, `mp_11_l_savanna` and `mp_17_dunes` each list the same 187 entries. Of
the grasses only `1_grasssavannah_b`, `_f`, `_g`
and `_bold`, and `2_sorghum`, have a `playerCoverage` of 1. The multiplayer grasses (`*_mp`), the
short and `_a`/`_c` savannah grasses, and every jungle and desert grass have 0: they are drawn, but
hide no one.

## Not mapped

Left deliberately unresolved rather than guessed:

- Bit 4 of the per-cell `Flags` byte. Bits 3..0 are the quad mask and bits 7..5 the surface-type
  palette slot — see [`.sdat`](../file-formats/sdat.md#the-low-nibble-is-the-quad-mask).
- The alpha of a tuft's third colour word, and how the editor scatter picks a tuft's height scale.
- `CFCXEditorCollectionManager::LoadMask` has an in-place expansion path for a shorter-than-expected
  mask file, which reads like nibble unpacking but whose index arithmetic is `i >> 2` where `i >> 1`
  would be expected for two nibbles per byte. Worth checking against a real mask file before relying
  on it.
