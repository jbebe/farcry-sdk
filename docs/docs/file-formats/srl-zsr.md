---
sidebar_position: 10
---

# `.srl` / `.zsr` — Per-Sector Sound Regions & Zones

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against `FarCry2_server`. Covers the container/attribution level — exact
per-sector size, owning manager class, and grid indexing are confirmed; the internal record layout
within each sector's blob is not (see Unknowns).
:::

Two of the three files found alongside `.sdat` in every world sector (see [`.sdat`'s sibling-files
note](./sdat.md#sibling-per-sector-files-srl-and-zsr) for the original hash-list discovery — 14,964 of
each across the install, matching the sector count exactly): `generated\worldsectors\sectorN.srl` and
`generated\worldsectors\zonesectorN.zsr`.

## What the acronyms mean

Both extensions are exported by name-obvious functions on `CFCXEditorDocument` (the map editor's own
export driver, same class family that writes `.sdat` — see `ExportSDAT` there):

- **`.srl` = "Sound Region Layer"** — written by `ExportSoundRegionLayer` (`0x08cb1930`).
- **`.zsr` = "Zone Sector"** — written by `ExportZones` (`0x08cb42a0`-ish, caller of `CZoneLogicManager::GetSectorData`).

## Container: none — a raw, fixed-size per-sector memory dump

Unlike `.sdat` (a real chunked container with a header, metadata block, and record array — see
[`.sdat`](./sdat.md)), both of these are dead simple: a single fixed-size raw block per sector, written
with no header and no framing at all.

```cpp
// ExportSoundRegionLayer, one call per sector (x, y each 0..7):
void* data = CAmbianceManager::GetSectorData(x, y);   // this+0xb8, a grid of block pointers
file.Write(data, 0x400 /* = 1024 bytes, exact */);

// ExportZones, one call per sector (x, y each 0..7):
void* data = CZoneLogicManager::GetSectorData(x, y);  // this+0xdc, a grid of block pointers
file.Write(data, 0x1000 /* = 4096 bytes, exact */);
```

- **`.srl` is exactly 1024 bytes**, always. `CAmbianceManager::GetSectorData` indexes a grid of
  pre-allocated block pointers at `this+0xb8` by `y * gridWidth + x` — no per-sector size variation.
- **`.zsr` is exactly 4096 bytes**, always. `CZoneLogicManager::GetSectorData` indexes a similar grid at
  `this+0xdc`, but — unlike ambiance — with an origin offset subtracted from both coordinates
  (`this+0xf4`/`this+0xf6`), meaning the zone grid isn't guaranteed to start at world sector `(0,0)`.
- Both confirm the same 8×8-sectors-per-level grid used everywhere else in this engine.

## What's semantically inside each block

Neither format's raw on-disk bytes were decoded field-by-field, but the class families that own this
data give strong content-type context:

**`.srl` (ambiance/sound regions)** — a rich, time-of-day-driven soundscape system:
`SSoundRegion` (the base record), `SSoundRegionLevel` (a volume/intensity level, sortable via
`SSoundRegionLevelSort`), `SSoundRegionTimeEntry`/`SSoundRegionTimeTrack` (time-scheduled sound events,
sortable via `SSoundRegionTimeSort`), `SSoundRegionTimeRandomFx`/`SSoundRegionRandomFx`/
`SSoundRegionRandomFxSound` (randomized ambient one-shots), `SSoundRegionVirtualName` (a named
sound-bank reference), `SSoundRegionTrackSetVolume` (a volume-control track event) —
`SSoundRegionManager` is the owning collection type.

**`.zsr` (gameplay zones)** — `CZoneLogicRegion : CBasicRegionEntity` is the per-zone record, which
self-registers into `CZoneRegionManager::AddRegion` on construction and default-initializes three
floats to `1.0` (offsets `+0xe4`/`+0xe8`/`+0xec` — plausibly an RGB tint or a scale/intensity triple, not
confirmed). `CZoneInfoComponent` is the entity-side hook (an `IEntityTask`-style component, same pattern
as every other per-entity capability documented in [Engine
Architecture](../engine-internals/architecture.md)) that presumably lets a placed entity define or
query which zone it's in.

## Confirmed owner and grid shape

`%ssector%d.srl` is referenced only by `ExportSoundRegionLayer`, and the resource class is
`CSRLResource` — so `.srl` is the sound region layer, not a general serialization blob as the
extension suggests.

Both files are fixed-size per-cell byte grids: `.srl` is 1,024 bytes (32×32, one byte per 2×2 quads)
and `.zsr` is 4,096 bytes (64×64, one byte per quad). `.srl`'s low nibble tracks biome — sampled
across `world1`, Desert sectors are 97.9% `0x00`, Jungle values end in `1` and Woodland values end in
`2`, with the high nibble varying within a biome. That correlation is ambient sound following the
biome, not vegetation: vegetation placement lives in the
[landmark files](../engine-internals/terrain-and-vegetation.md#it-lives-in-the-landmark-files).
`.zsr` is bimodal per sector — a sector is either almost entirely `0xFF` or almost entirely covered —
which reads as zone membership rather than a painted per-cell field.

## Unknowns

- **The actual on-disk record layout for either format.** `SSoundRegion` contains a `std::string`
  member (a name/virtual-name field) — a runtime C++ object with a heap pointer can't survive a direct
  `memcpy`-to-disk-and-back round trip, so the raw 1024-byte `.srl` blob is very unlikely to be a literal
  array of live `SSoundRegion` objects. Either there's a separate, flattened POD structure this data
  gets serialized to/from (not yet located), or `CAmbianceManager`'s per-sector block holds something
  simpler than the full runtime record set and the richer `SSoundRegion*` family is reconstructed
  elsewhere at load time. Not resolved.
- Whether `.zsr`'s `CZoneLogicRegion` records serialize any more directly — `CBasicRegionEntity`'s own
  fields weren't traced.
- The exact meaning of `CZoneLogicRegion`'s three default-`1.0` floats.
- Whether either file has *any* internal structure (record count, per-record size prefix) or is purely
  a fixed-layout struct array with a size implied entirely by the constant 1024/4096-byte total.
