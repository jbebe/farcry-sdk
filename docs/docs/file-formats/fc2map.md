---
sidebar_position: 13
---

# `.fc2map` — Editor Map Document

:::info[Verified via reverse engineering]
Container contents established from `tools/misc/hash-lists/map_files.filelist` (the manifest of a
shipped map's file set); the sector-grid bound and slot naming traced via GhidraMCP against
`Dunia.dll` and `FarCry2_server`.
:::

The document format written by the stock map editor (`FC2Editor.exe`). Despite the distinct
extension, it is not a bespoke editor format: it is a **cooked level tree**, built from the same file
types the retail game loads.

## Contents

A `.fc2map` holds 583 files in four groups:

| Group | Count | Purpose |
|---|---|---|
| `ige/{map.xml, heightmap.raw, texture.mask, collection.mask}` | 4 | authoring source — the editable representation |
| `levels/ige_map/generated/sdat/sd<n>{,_color,_diffuse,_mask,_shadow}` | 64 × 5 = 320 | cooked terrain textures — see [`.sdat`](./sdat.md) |
| `levels/ige_map/generated/worldsectors/{worldsector<n>.data.fcb, sector<n>.desc.fcb, sector<n>.srl, zonesector<n>.zsr}` | 64 × 4 = 256 | cooked sector payloads — see [`.fcb`](./fcb.md) and [`.srl`/`.zsr`](./srl-zsr.md) |
| `worlds/ige_map/generated/{<name>.game.xml, <name>.mapsdata.fcb, <name>.sectorsdep.fcb}` | 3 | world-level metadata |

The split matters: `ige/` is the only part the editor can re-open for editing. Everything under
`levels/` and `worlds/` is cooker output, regenerated on save.

Because the container already holds cooked output, producing a level tree needs only unpacking — not
the editor's export path, which does not work in retail (see below).

## Identical in kind to retail levels

A retail single-player level (`levels/w1_a_1/`) contains `worldsector<n>.data.fcb`,
`sector<n>.desc.fcb`, `sector<n>.srl`, `zonesector<n>.zsr` and an `sdat/` folder — the same file
types, differing only in count and in extras the editor never emits. The editor is therefore not
writing a parallel or reduced format; it writes the engine's real level format.

`ExportWorldSectors` additionally tags serialized objects with `Entity`, `MissionLayer`, `PathId` and
`WorldSector` type markers, so the container carries mission-layer scoping even though the editor
exposes no way to author it.

## The sector grid is fixed at 8×8

`ExportWorldSectors` iterates a literal `for j in 0..7 { for i in 0..7 }` against a 64-entry
allocation. The editor cannot produce a map with any other sector count.

| Map kind | Sectors | Grid |
|---|---|---|
| Editor (`.fc2map`) | 64 | 8×8 |
| Retail multiplayer | 100 | 10×10 |
| Retail single-player level | 256 | 16×16 |

Retail multiplayer maps exceed the editor's own limit, which places the 64-sector cap in the editor's
export path rather than in the engine.

Sector edge length is constant at **64 world units** across every map kind, so grid dimension scales
directly with world extent (editor 512, multiplayer 640, single-player world 5120).

## Sector numbering

Editor maps number sectors densely, `0`–`63`. Retail levels use a global world-grid index instead:
`w1_a_1` runs `5120`–`6335` in bands of 16 with a stride of 80, i.e. `sectorId = row × 80 + column`
over the parent world's 80×80 grid.

Any per-sector satellite file keys off the same id — [`nv_<sectorId>.nvm`](./nvm.md) indices match
`sector<sectorId>.desc.fcb` exactly for a given level.

## The `ige_map` slot

The editor cooks into one fixed level/world slot named `ige_map`, hardcoded as a string literal with
eleven references across eight functions (`FCE_Document_Save`, `ExportMapData`,
`ExportSectorDependencies`, `PerformSave`, `Display`, and three others). Saving stages through
`<personal>/ingameeditor/levels/test/` before packing.

`worlds/tmpla/` is the un-stripped development twin of this slot: its `entitylibrary.fcb` and
`managers.fcb` are byte-identical to `ige_map`'s, but it additionally ships
`entitylibrary_full.fcb`, `<name>.game.xml`, `<name>.mapsdata.fcb`, `<name>.sectorsdep.fcb`, a
navmesh index, and the `_depload` / `_deploadnewparticles` source XML alongside the binaries. It is
the reference for anything `ige_map` appears to lack.

## What the cooker does not emit

Relative to a retail level, editor output omits:

| Artifact | Present in | Consequence |
|---|---|---|
| `levels/<lvl>/generated/nv/sectors/nv_<n>.nvm` | single-player levels only | no AI navigation data — see [`.nvm`](./nvm.md) |
| `sector<n>.preload_x.fcb` | single-player levels | no streaming preload data |
| `landmarkfar_<n>.data.fcb` / `landmarknear<n>.data.fcb` | single-player and retail multiplayer | no distant-silhouette LOD geometry |
| `mapcompass.xbt` (+ mip) | every retail world | no in-game map/compass texture |
| `<name>_deploadnewparticles.xml` | retail worlds | binary `.rml` only — see [depload](./depload.md) |
