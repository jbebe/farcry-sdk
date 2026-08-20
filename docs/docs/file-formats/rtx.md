---
sidebar_position: 16
---

# `.rtx` — Realtree Vegetation

:::info[Verified via reverse engineering]
The geometry below is read out of `RTxcManager::LoadSkeletal`, `RTxcManager::SetSkeletalPointer` and
`RTxcSkeleton::InitLOD` in the symbol-bearing `FarCry2_server` binary, and every stride is confirmed
against all 104 shipped `.rtx` files. The simulation state those structures also carry — wind,
defoliation, fire, branch breaking — is not decoded.
:::

`.rtx` is the asset format behind Far Cry 2's vegetation: the trees and large plants that sway, burn,
shed branches and lose leaves. It is a simulation asset rather than a static mesh; see
[`RTxcManager`](../engine-internals/architecture.md#rtxcmanager--rtx-is-a-live-vegetation-simulation-not-a-static-mesh)
for the runtime class taxonomy.

A species is a **branch skeleton** — a tree of tapered tube segments — with foliage hung on it, as
either flat cards or modelled leaf meshes. Nothing but the modelled leaves ships as triangles: the
branches are skinned from the skeleton at runtime.

## Inventory

104 `.rtx` files ship, all under `graphics\vegetation\<biome>\realtrees\`, mostly in **`worlds.fat`**
with eight more in the DLC archives. Observed sizes run from 40 KB to 250 KB.

## It is a memory image, not a chunked file

The file is a dump of one heap allocation. Every internal pointer was a live address on the machine
that saved it, and **no offsets are stored** — the engine recovers them by re-walking the arena in
the order it originally packed it, which is what `SetSkeletalPointer` does. A reader has to repeat
that walk exactly; one stride wrong and everything after it desynchronises silently.

The stale pointers are still in the file, and they are useful: subtracting the save-time base
recovers the original offset of every array, which is how the walk below was checked.

## Container framing

```
+0x00  u32   size of section A
+0x04  u32   size of section B (0 when absent)
```

`sizeA + sizeB == fileSize - 8`. Section B is a trailing block that `LoadSkeletal` is not given and
that nothing below reads. The header the engine parses begins at **+0x08**, and every offset in this
section is relative to that:

```
+0x00  u32     0x88 — version tag, rejected if it is anything else
+0x04  u16     tree type (asserted < 4)
+0x06  i16     variant
+0x08  u32     size of an optional prefix block (0 in every shipped file)
+0x0C  u32     size of the skeleton arena
+0x10  char[256]  asset path, e.g. graphics\Vegetation\Desert\Realtrees\HY_Aloes_01
```

The skeleton arena follows at `+0x118` (after the optional prefix block, which never occurs), runs
for the size at `+0x0C`, and the material table follows it.

The embedded path often ends in **`.rta`**, an authoring extension that ships in no archive.

## The skeleton arena

Its first `0x220` bytes are the `RTxcSkeleton` header. The counts that drive the walk:

| Offset | Meaning |
|---|---|
| `+0x10` | node count |
| `+0x1C` | branch count |
| `+0x28` | leaf-card count |
| `+0x34` | modelled-leaf count |
| `+0x180` | count of a simulation array, stride `0x34` |
| `+0x220` | render block, `0x20` bytes — counts at `+0x08` and `+0x0C` size two more arrays |
| `+0x240` | location block, `0x20` bytes |

After the two blocks the arena is a bump allocator: each array is placed at the cursor and the cursor
rounded **up to 16**, bar two that the engine leaves tight against the next. In order:

| Array | Count | Stride | Padded |
|---|---|---|---|
| modelled-leaf pointer table | `+0x34` | 4 | yes |
| one record per modelled leaf | — | variable, see below | — |
| simulation array | `+0x180` | `0x34` | yes |
| **branches** | `+0x1C` | `0x28` | yes |
| simulation array | `+0x1C` | `0x2C` | yes |
| **node records** | `+0x10` | `0x4C` | yes |
| **node geometry** | `+0x10` | `0x20` | **no** |
| **leaf cards** | `+0x28` | `0x5C` | yes |
| per-node `u16` list | `u16` at node record `+0x2E` | 2 | once, after all nodes |
| simulation array | `+0x10` | `0x48` | yes |
| simulation array | `+0x28` | `0x18` | yes |
| simulation array | render block `+0x0C` | `0x54` | yes |
| simulation array | render block `+0x08` | 4 | yes |
| **node poses** | `+0x10` | `0x20` | **no** |
| **leaf-card poses** | `+0x28` | `0x20` | **no** |
| modelled-leaf render vertices | render block `+0x0C` | `0x3C` | yes, and last |

That last array is a deinterleaved copy of the finest level of the modelled leaves, and it is the
only thing between the leaf-card poses and the end of the arena. So a correct walk always accounts
for the arena exactly — a useful integrity check on a format with no chunk tags.

### Branches

`0x28` bytes; only the first two fields are geometry.

```
+0x00  i32   first node
+0x04  i32   segment count
```

The run is **inclusive at both ends**: a branch owns nodes `first` through `first + count`. Across
all 104 files the branches partition the node list exactly, with no node in two branches and none
left out.

### Nodes

Position and direction come from the pose array, radius and length from the geometry array — the
split `RTxcSimulation::GetNodeInfoStatic` reads.

Pose, `0x20` bytes:

```
+0x00  float[3]  position
+0x0C  float     radius (the same value as below)
+0x10  float[3]  direction, unit length
```

Geometry, `0x20` bytes:

```
+0x08  float  radius
+0x0C  float  length
```

**Length is the distance to the next node along the branch** — exact in every file — so a branch is
one continuous tube. On a branch's last node it is the tip extension instead.

Coordinates are metres, **Z up**, and the trunk's first node usually sits slightly below the pivot so
the tree meets the ground.

### Leaf cards

`0x5C` bytes, positioned by the leaf-card pose array (same `0x20` layout as a node pose).

```
+0x08  float     card radius
+0x0C  float[3]  unit vector
+0x18  float[3]  unit vector
+0x24  float[3]  corner offset
+0x30  float[3]  corner offset
+0x3C  float[3]  corner offset
+0x48  float[3]  corner offset (the negation of the one at +0x3C)
```

All four corner offsets sit at the card radius from the leaf's position, but they are measurably
**not coplanar** — only 1,569 of 20,107 shipped cards are — so a card is a twisted quad rather than a
flat one. The file does not record which order the corners go in; the three bytes at `+0x58` take
only 8 distinct values across the corpus and look like an ordering, but have not been confirmed.

Species with no foliage still carry a placeholder: both euphorbia cacti have exactly one card, of
radius 0.03.

### Modelled leaves

The jungle plants (`hy_*`) replace cards with real meshes — the `RTxsHybridLeaf` of the runtime
classes. Each is one record:

```
+0x120  i32     detail level count (3 in every shipped file)
+0x150  entry[] one per level, 0x10 bytes each
```

Each entry:

```
+0x00  i32  vertex count
+0x08  i32  index count
```

with the vertex block (padded) then the index block (padded) following the entry table, levels back
to back. Vertices are `0x94` bytes:

```
+0x00  float[3]  position, already in model space
+0x0C  float[3]  normal, unit length
+0x48  float[2]  UV
+0x60  i32[6]    neighbouring vertices
+0x7C  float[5]  rest distance to each
```

The rest is the leaf's own cloth simulation — which vertices it is pinned to and how far from each it
hangs. Indices are `u16` triangle lists.

A record also carries a nine-digit decimal string at `+0x1C`. (The earlier note on this page reported
a repeated decimal string in the payload and guessed at a build stamp; this is what it was.)

## Materials

Immediately after the arena, and the only part of the file that is not a memory image:

```
i32  count
  i32   slot
  i32   path length
  char  path[length]   (not NUL-terminated)
```

The slots are the argument order of `RTxcSkeleton::InitLOD`, which sets each up guarded by the count
that draws with it:

| Slot | Draws |
|---|---|
| 0 | branches (bark) |
| 1 | leaf cards |
| 2 | modelled leaves |

Every file names exactly two: slot 0 plus slot 1, or slot 0 plus slot 2 — never both kinds of
foliage. That matches the geometry: a species has leaf cards or modelled leaves, never both.

The paths name a **`.mlm`**, which ships in no archive. The material that does ship is the `.xbm` of
the same stem — `graphics\_materials\smaingot-M-2007100145957056.mlm` resolves to
`graphics\_materials\smaingot-m-2007100145957056.xbm`. This corrects an earlier note on this page:
the payload *does* reference other assets, just as plain strings rather than as the path hashes the
rest of the engine keys on, which is why a hash scan found nothing.

## Placement

`.rtx` names a species, not a location. Placement is resolved: it lives in the per-sector landmark
files, under a `CCollectionComponent`'s `VegetationZoneData`, and a `.rtx` is referenced there the
same way a mesh is — by the CRC32 of its own path. See
[It lives in the landmark files](../engine-internals/terrain-and-vegetation.md#it-lives-in-the-landmark-files)
and [Resource ids are path hashes](../engine-internals/terrain-and-vegetation.md#resource-ids-are-path-hashes-and-most-of-the-scatter-is-grass).

RealTree is the smaller half of that scatter: 60 distinct `.rtx` resources against roughly 101,000
placed instances in `world1`, about 4% of the total. The other 96% resolves to ordinary `.xbg`. By
*species* it is nearly everything, though — `rt_tree_acacia`, `rt_tree_ficus_a`/`_b`,
`rt_tree_camphor`, `rt_saguaro_cactus_line_*`, `rt_bush_tamarix_*`, `hy_aloes_*`, `hy_banana_big` —
while the `.xbg` share is dominated by grass and by `terrain\rocks\desert\ter_desertrock*`.

## Impostor cards

Six meshes under `graphics\vegetation\jungle\realtrees\donotuse\` are camera-facing impostor cards:
`facingbush`, `facing_bush_large`, `facing_bush_palm`, `facingbush_savannah2`,
`facing_bush_large_savannah`, `facing_bush_savannah`. Despite the folder name the scatter does place
them in their own right.

Each is 12 vertices: a single quad (four triangles, normals along **−Y**) plus two ground-facing
triangles, spanning roughly 16 m wide and up to 15 m tall.

They are also the only meshes in the game that ask to be turned toward the viewer, and they say so in
their material rather than their name. Exactly **two of the 2,208 shipped `.xbm` materials** carry a
`Billboard` property — `dboivin-m-2008041150042062` and `smaingot-m-2008042358745658` — and between
them they cover those six meshes and nothing else. A renderer that ignores it draws a tree card
edge-on.

## Reading one

JackAll parses `.rtx` and tessellates the skeleton into triangles, which is how the map editor's
vegetation layer draws real species rather than stand-ins —
`tools/JackAll/src/JackAll.Tools/Rtx/`. Sizes come out where they should: a kapok at 46 m, a saguaro
at 5.2 m, an aloe at 1.3 m.
