---
sidebar_position: 14
---

# `ingameeditor\*_inventory.xml` — Editor Palettes

:::info[Verified via reverse engineering]
Schema traced via GhidraMCP against `FarCry2_server` (`CFCXEditorObjectEntry::Load`,
`CFCXEditorObjectInventory::Load`, `CFCXEditorObject::LoadEntities`) and cross-checked against the
shipped files extracted from `common.dat`/`patch.dat`.
:::

The map editor's palettes — what can be placed, painted, or generated — are plain UTF-8 XML in the
game archives, not compiled [FCB](./fcb.md). They contain no engine logic: an entry is a descriptor
naming something the engine already knows how to spawn.

Both `common.dat` and `patch.dat` carry `object_inventory.xml`; `patch.dat` wins, so the palette is
overridable through the ordinary [archive](./archives-fat-dat.md) layering with no code change.

## `object_inventory.xml`

```xml
<ObjectInventory>
  <Directory Id="…" Display="…" [PcOnly="1"]>
    <Directory Id="…" Display="…">
      <Entry Id="…" Display="…" SourceType="0" SourceName="…" ObjectCost="…" />
      <Entry …>
        <Pivot Pos="x,y,z" Normal="x,y,z" NormalUp="x,y,z" />
      </Entry>
```

`Directory` nests at most two levels. `PcOnly` filters an entry or directory out on console builds.

### `Entry` attributes

| Attribute | Type | Meaning |
|---|---|---|
| `Id` | string | palette-unique key, hashed to the `u32` returned by `FCE_Inventory_Object_GetId` |
| `Display` | string | UI label — a literal, not a localization key |
| `SourceType` | int | `0` = archetype, `1` = prefab |
| `SourceName` | string | the archetype or prefab to spawn |
| `ObjectCost` | float | budget units consumed, read by `FCE_BudgetManager_*` |
| `Layer` | string | mission-layer scope, stored as a `CPathID` |
| `VisualMesh` | string | `.xbg` proxy mesh, for entries with no visual of their own |
| `VisualIcon` | int | index into `ingameeditor\object_icons.xbt` |
| `ZOffset` | float | vertical placement offset |
| `IsPhysic`, `IsVehicle`, `IsFlock`, `GameObject`, `WorldObject` | bool | classification bits |
| `ShowInMinimap`, `SnapPhysics`, `AutoOrientation`, `AutoPivot` | bool | behaviour flags |

`Pivot` children supply snap points (position, surface normal, up vector) for entries that connect to
each other, such as fences and walls. `AutoPivot` generates them instead.

### How an entry resolves

`CFCXEditorObject::LoadEntities` dispatches on `SourceType` alone:

```
SourceType 0 → CreateFromArchetype(SourceName)
SourceType 1 → CreateFromPrefab(SourceName)
VisualMesh set → CreateHelper(VisualMesh)
```

`SourceName` is a plain dotted string compared by value — not a hash and not an asset path:

- **Archetype**: matches an entity's `hidName` in the world's `generated\entitylibrary.fcb`
  byte-for-byte. The first segment is the library root, the rest is the group path and name.
- **Prefab**: matches a `Description` name in `CPrefabManager` inside `<world>.managers.fcb`. A
  prefab is itself a list of objects, each with its own `SourceType`/`SourceName`/position/angles.

There is no allowed-archetype list anywhere in the loader. The palette is a curation layer over
whatever the loaded world's entity library already contains, so extending it is a data-only edit.

### Entry counts

The shipped file declares 1058 live entries across 35 directories. A further 1609 entries sit inside
a single XML comment spanning three directories (`DONOTUSE_Objects`, `DONOTUSE_Archetypes`,
`DONOTUSE_Prefabs`) — a naive count of `<Entry` tags overshoots by that amount. Almost all of the
commented archetype references still resolve against the shipped entity library.

## Sibling palettes

Same `Id`/`Display`/`Directory` idiom, differing only in the reference field:

| File | Live entries | Reference field → target |
|---|---|---|
| `object_inventory.xml` | 1058 | `SourceName` → entity-library archetype or manager prefab |
| `collection_inventory.xml` | 29 | `CollectionName` → `Collection` in `<world>.managers.fcb`; also carries `ZoneLogic`, `SoundRegion`, `SoundIntensity`, `SectorCost` |
| `texture_inventory.xml` | 27 | `TextureId` → `graphics\terrain\_textures\<biome>\<id>_d.xbt`; each entry carries ten `<Color>` children for the minimap palette |
| `spline_inventory.xml` | 15 | `Material` → `graphics\_materials\editor\Road_*.mlm`; plus `Tessellation`, `WidthScale`, `TextureScale`, `LOD0Distance` |
| `wilderness_inventory.xml` | 7 | `ScriptPath` → `ingameeditor\wilderness\<biome>.lua` — see [the Wilderness language](../engine-internals/wilderness-script-language.md) |
| `ambience_inventory.xml` | 8 | `AmbienceId` → small integer enum |

## Entry ids are CRC32

`CStringID` hashes the raw `Id` string with standard CRC32 (case-sensitive, `0xEDB88320` polynomial,
`0xFFFFFFFF` init, final XOR). The editor's object-thumbnail cache is keyed by that value, writing
`%TEMP%\FarCry2\Editor\<id>.png` per rendered entry — which makes the cache directory a direct
readout of which palette entries the engine accepted and successfully rendered.

The `Id` string is also reused as a localization key in the `InGameEditor_Objects` string table, with
`.` replaced by `_`. Because `Display` carries a literal label, entries added by a mod need no
string-table work.
