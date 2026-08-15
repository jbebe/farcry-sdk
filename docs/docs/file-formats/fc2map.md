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
types the retail game loads, wrapped in the engine's ordinary game-file container.

## Container

A `.fc2map` is a **`CCustomMapGameFile`** — the same `CGameFileHeader` family as a campaign save (see
[savegame](./savegame.md)), distinguished by the type tag in the first word:

| Tag at `0x00` | File |
|---|---|
| `10` | campaign save |
| `11` | custom map (`.fc2map`) |

`CGameFileHeader::SaveToFile` writes the 20-byte base as `u32` type tag, `u32` version and 12 further
bytes. `CCustomMapGameFileHeader::SaveToFile` then writes, in this order:

```
u8[8]                     two IDs; 0xFFFFFFFF/0xFFFFFFFF when unset
string                    length-prefixed, no terminator - the author name
u8[8]                     two IDs
string                    length-prefixed
string                    length-prefixed - the map name
u8[8]                     two hashes
u8[44]                    struct tm - the save time
u8[44]                    struct tm
u32, u32, u32
```

`GetSaveSize()` returns `CGameFileHeader::GetSaveSize() + 0x7c + ` the three string sizes, and the
fixed fields sum to exactly `8+8+44+44+4+4+4 = 124 = 0x7c`. `LoadFromFile` mirrors this field for
field, so the layout is fixed from both directions.

The 44-byte blocks are 11 `u32`s — the same shape as the three time blocks `CMapInfo` carries, each
copied as an eleven-word loop. The first decodes to the real save time: a map saved on 14 August 2026
at 12:01:34 yields `sec=34, min=1, hour=12, mday=14, mon=7, year=126, wday=5, yday=225`, all
internally consistent.

Verified against three independent maps whose header strings differ in length: the author name comes
back as the creator entered it, and the map name as `"Untitled map"` in all three. The second string
is evidently taken from a UI control and can capture junk — one sample holds the Win32 window class
`WindowsForms10.BUTTON.app.0.378734a`.

## Screenshot and tail

`CScreenShot::WriteToFile` writes four `u32`s — width, height, channels, bits per channel — then
`width * height * channels * bpp / 8` bytes of BGRA pixels, then a `u32` metadata count and that many
metadata records. Every observed map carries a 128×128×4×8 thumbnail, so 65,536 bytes of pixels, and
a zero metadata count.

Between the pixels and the payload sit a `u32`, a length-prefixed GUID string (a null GUID in the
maps observed, empty in one written by a stripped host) and 24 further bytes, zero in every sample.

The four dimension words are the header's own last four `u32`s, with the pixels following
immediately: at the computed end of the header — 168, 175 and 211 across three maps whose strings
differ in length — the bytes are BGRA pixel data every time, and the sixteen preceding bytes are
`128, 128, 4, 8` in all three. There is no room for, and no trace of, any field between them.

This diverges from the Linux dedicated server, whose `CCustomMapGameFileData::SaveToFile` writes a
string before the screenshot and whose header ends `44, 44, 4, 4, 4` rather than `44, 40` plus the
four dimension words. The two builds' structures differ here; the PC layout above is the one that
holds on disk.

The write is entirely engine-side. The managed editor's `EditorDocument.Save` passes only a directory
and file name to `FCE_Document_Save`, and the binding layer contains no file-writing code at all —
`FCE_Document_TakeSnapshot` hands the engine an image, and the engine embeds it.

:::caution[Open]
The 24-byte tail after the GUID string is zero in every sample available, so its field split cannot
be recovered by reading files alone.
:::

The payload that follows is a **`CCryArchive`** — the same archive family as the game's
[`.fat`/`.dat`](./archives-fat-dat.md) pairs. `CFCXEditorDocument` treats the document as a virtual
filesystem over it: `OpenFile`/`CloseFile`/`FileExists` map onto `CCryArchive::FileOpen` for reads
and, for writes, buffer into a `CMemoryStreamFile` that `CloseFile` hands to
`CCryArchive::AddFileNotCompressed`. **Entries are therefore stored uncompressed.**

`CCryArchive::SaveFat` writes the index as:

```
u32   0x46415432          magic
u32   5                   latest FAT version
u32   (fatVersion << 8) | platform
u32   entryCount
      entry[entryCount]   16 bytes each
u32   extraCount
      extra[extraCount]   12 bytes each
```

The FAT magic does not appear as plain bytes anywhere in a `.fc2map`, because each of the archive's
three streams is individually compressed — see below.

### Payload header

`CFCXEditorDocumentArchive::OpenForRead` reads a 20-byte header at the payload's start and refuses
the file unless the first two words match:

```
u32   0x4D324346          magic, "FC2M"
u32   1                   version
u32   dataOffset          always 0x14, i.e. immediately after this header
u32   fatOffset
u32   nfoOffset
```

All three offsets are relative to the header's own start, and the NFO stream runs to end of file.
Each of the three resulting spans is wrapped in a `CCompressedFile` and handed to
`CCryArchive::CCryArchive(name, forWrite, fat, data, nfo)`.

### `CCompressedFile` block container

Each stream is a sequence of independently compressed blocks with a seek index at the end:

```
u32   indexOffset         relative to the stream's start
      <block>...          raw zlib streams, each inflating to 0x40000 bytes (the last one partial)
at indexOffset:
u32   count
      entry[count]        u32 uncompressedOffset, u32 compressedOffset | 0x80000000
```

There are `count - 1` blocks; the final entry is a sentinel carrying the stream's total uncompressed
size and, as its compressed offset, `indexOffset` itself. The block size is the `0x40000` the
`CCompressedFile` constructor is given at both open sites.

### The inner archive is a stock game archive

Once the three streams are inflated, the FAT is an ordinary version-5 `.fat` index and the NFO is
plain XML — one `<File Path Crc FileTime />` element per entry, keyed by the same CRC32 the FAT uses.
Nothing about them is editor-specific, so JackAll's existing `.fat`/`.dat` reader consumes them
unchanged: writing the inflated FAT and data streams out as a `.fat`/`.dat` pair and running
`jackall-cli archive extract` recovers all 583 files with every name resolved.

Measured on a freshly saved empty map: 8,038,082 bytes of data across 31 blocks, a 9,348-byte FAT
(583 entries, zero extras) and a 33,495-byte NFO, from a 126,109-byte file. The `ige/` group's
fixed-size grids come out as 513×513 `u16` for `heightmap.raw`, 1024×1024 for `texture.mask` and
512×512 for `collection.mask`.

### Section order

`CCustomMapGameFileData::SaveToFile` writes, after the header above:

```
string                    length-prefixed
CScreenShot               the map snapshot - 16-byte header, pixels, metadata
u32  count
string[count]             required-DLC ids, same idiom as a campaign save
<blob>                    the archive, written straight from a CMemoryStreamFile
```

The blob is produced by `CCustomMapGameFileData::SaveToMemory`, which is a single call:

```c
CFCXEditorDocument::DoSaveAndExport(document, "ige/", "ige_map", memoryStream);
```

That is the whole cooker in one entry point — authoring files under the `ige/` prefix, cooked output
under the fixed `ige_map` level and world names, all into one in-memory archive. Anything that wants
to build a `.fc2map` from outside the engine has to reproduce exactly that call's output.

`CCustomMapGameFileData::LoadFromFile` mirrors this exactly — string, screenshot, DLC list — and then
hands everything from the current position to end of file to `LoadToMemory`, which is what fixes the
payload's start. `SaveToFile` writes the memory stream's bytes verbatim, so the container itself adds
no compression; all of it lives in the three `CCompressedFile` streams inside the payload.

## Authoring source

`CFCXEditorDocument::DoSave` builds an XML tree rooted at `FarCry2.Editor.Map` with child nodes
`ObjectManager`, `CollectionManager`, `TerrainManager`, `SplineManager` and `Properties` — each
produced by `CNomadObject::Save` on the corresponding manager singleton — then writes `map.xml`,
`heightmap.raw` (`CFCXEditorTerrainManager::SaveHeightmap`), `texture.mask` (`SaveTextureMask`) and
the collection masks (`CFCXEditorCollectionManager::SaveMasks`). Those four are the `ige/` group
below.

### Object placement

`ige/map.xml`'s `<ObjectManager NumObjects Version="4">` holds one element per placed object:

```xml
<Object crc_Entry="4131167615" Pos="260.721,251.477,16" Angles="0,0,0" Index="1" />
```

`crc_Entry` is the CRC32 of the palette entry's `Id` attribute — the same
[`CStringID`](../engine-internals/overview.md) hash used everywhere else — so placements resolve
against [`object_inventory.xml`](./object-inventory.md) by hashing each entry's `Id`. An id absent
from the stock palette identifies a map authored against a modified one, which makes the field a
usable provenance check.

Position and angles are the only per-object state: there is nowhere in this format to record a
property override, which is the same gap the `FCE_Object_*` API has.

### Terrain

`ige/heightmap.raw` is a bare grid of little-endian `u16` height samples, 513×513 for the editor's
8×8-sector world — one sample per world unit, sectors sharing their touching edge (`8 × 64 + 1`).
The samples are the **same encoding the cooked [`.sdat`](./sdat.md) sectors use**: every value equals
its sector cell's raw height, unscaled, verified across all 4225 cells of every sector of a saved
map. Retail campaign terrain therefore transfers verbatim, and a flat default map stores 2048
everywhere — 16 m at the engine's 1/128 scale, which matches where objects come to rest.

`ige/texture.mask` is 512×512 cells of 4 bytes, one cell per world unit. The bytes are splat weights
for the four slots `map.xml` declares, ordered **`[slot2, slot1, slot0, slot3]`**, and they sum to
255 per cell — painting one texture at full strength gives `255` in its byte and `0` in the rest,
with blended values only along brush edges.

`ige/collection.mask` is 512×512 single bytes indexing the eight `CollectionManager` slots.

`map.xml`'s managers name what those masks index:

```xml
<CollectionManager>
  <collectionEntries> 8 × <Entry crc_id=…> </collectionEntries>
  <collectionSeeds>   8 × <Seed id=…>      </collectionSeeds>
</CollectionManager>
<TerrainManager StormFactor WaterLevel>
  <textureEntries>    4 × <Entry crc_id=…> </textureEntries>
  <TimeOfDay Rep=…/>
</TerrainManager>
```

Each `crc_id` is the CRC32 of an inventory entry's `Id`, `0xFFFFFFFF` when the slot is unused — the
same convention as object placements. **Four terrain textures per map is an authoring limit, not an
engine one**: the engine assigns four *per sector* and varies them across a world, which is what
`DetailTexMask` in [`sector#.desc.fcb`](./sdat.md#terrain-layers-and-detailtexmask) records. The
editor simply writes one global set into every sector it cooks.

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
