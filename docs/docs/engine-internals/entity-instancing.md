---
sidebar_position: 14
---

# Entities — Archetype Resolution and Instancing

:::info[Verified via reverse engineering]
Traced via GhidraMCP against `FarCry2_server` (`CEntityLibraryManager::BuildArchetypesMap`,
`CEntitySystem::SpawnEntityFromNode`, `CReadOnlyMergeNode::BuildChildrenEntries`) and `Dunia.dll`
(`CXGame::LoadArchetypes`), with counts measured from a retail install.
:::

An object in the world is described by up to three things at once: the library that defines its
archetype, a library that overrides that definition, and the placed instance itself. This page covers
how the engine collapses those into one live entity — which decides, for a modder, **which file an
edit has to go in to be visible at all**.

## The archetype table is one name-keyed map

`CEntityLibraryManager::BuildArchetypesMap(SerializableNodeRef const&)` walks a library's categories,
then each category's `EntityPrototype` children, reads the `hidName` of each prototype's `Entity`
child, and inserts into a single

```cpp
hashtable<CNoCaseStringID, ISerializableNode const*>
```

Three properties follow directly from that code, and any tool that reproduces it must match all
three:

- **The key is the fully qualified `hidName`** — `Animals.Quadrupeds.CapeBuffalo`, not the
  prototype's own shorter `Name` attribute.
- **Matching is case-insensitive**, because the key type is `CNoCaseStringID`.
- **Insertion replaces.** It walks the bucket chain for an existing entry with that id and, when it
  finds one, overwrites the node pointer instead of appending. Nothing is merged field by field at
  this level: a later definition replaces an earlier one whole.

Two callers fill that one map — `ReadFromXML` for a base library and `Override` for an override
library — so **the last library loaded wins**.

## Which libraries load, and in what order

`CXGame::LoadArchetypes` in the client:

```
if (flag at +0xC4 == 0)  load "\entitylibrary.fcb"
else                     load "\entitylibrary_full.fcb"
                         load "generated\EntityLibraryPatchOverride.fcb"
                         then a loop over further libraries
```

The base is an **either/or**, not a stack — one of the two, never both. The patch override then loads
unconditionally *after* whichever base was chosen, so it wins over both.

The trailing loop is the DLC libraries. That is read directly in the dedicated server, where the
equivalent function calls `CDlcService::GetEntityLibraries` and passes each path it returns to
`CEntityLibraryManager::Override`; Dunia's loop matches in shape but has not been read as the same
call (see [Unknowns](#unknowns)). Their order among themselves is not established, and they load
after the patch, so a DLC library wins over it.

`entitylibrary_full.fcb` is the **client's** base: the dedicated server binary contains no reference
to the string anywhere, while the suffix-less library appears in both. Measured over `world1`:

| library | archetypes | relationship |
|---|---|---|
| `worlds\world1\generated\entitylibrary.fcb` | 1,419 | shared by client and server |
| `worlds\world1\generated\entitylibrary_full.fcb` | 5,566 | client only; strict superset, adds 4,147 |
| `generated\EntityLibraryPatchOverride.fcb` | 915 | loads last, wins |
| `worlds\ige_map\generated\entitylibrary.fcb` | 5,566 | identical content to `_full` |

**121** of the patch override's names are also declared by the world's own library, and **912** of
them are declared by `_full`. That second number is why the either/or matters: if the two bases
stacked, they and the patch would be contesting nearly every archetype the patch declares.

The replace-by-name rule also applies *within* one file: `_full`'s 5,735 prototype nodes carry only
5,566 distinct names, and the 169 redundant nodes belong to **29** names it declares more than once.
Only the last declaration of each survives the map. The base library and the patch override contain no
such duplicates.

## Instancing merges, it does not replace

`CEntitySystem::SpawnEntityFromNode(ISerializableNode const*)` branches on the instance's
`tplCreatureType`:

- **Absent** — the instance node is used directly. No archetype is consulted at all.
- **Present** — the archetype is looked up in `CEntityLibraryManager`, its `Entity` child taken, and
  the entity is loaded from a **`CReadOnlyMergeNode(archetype, instance)`** rather than from either
  node alone.

`CReadOnlyMergeNode::BuildChildrenEntries` defines the merge. It seeds its child array from the
**archetype**, then for each **instance** child reads that child's tag and scans for an unpaired
archetype child with the same tag:

| case | result |
|---|---|
| archetype has the child, instance does not | inherited unchanged |
| both have it | paired into a **nested** `CReadOnlyMergeNode`, recursing all the way down |
| only the instance has it | appended as a new child |

So an instance **overrides what it names and inherits the rest**, at node granularity, recursively.
It is a lazy read-only view over both trees — nothing is copied or flattened at load time.

Sampling 146 placed entities across `world1` sectors: **48** carry `tplCreatureType` and therefore
merge against an archetype; the other 98 stand alone. All 146 carry their own `Components` child, and
61 name their own `.xbg` mesh directly — which is why a renderer can draw most of a map without
opening a library, while a property inspector cannot.

## Components read off an instance

Two component layouts confirmed from shipped sector data. Both hang off an entity's `Components`
child and are read the same way whether they came from the instance or were inherited.

### `CDynamicLightComponent` — every placed light

There is no light file. Lights are a component on ordinary entities, named `OmniLight_*`,
`SpotLight_*`, `Lighting.*CampFire*` and similar; roughly 1,400 in `world1`, about half of them
spots, and many shipping disabled for mission logic to switch on.

| field | meaning |
|---|---|
| `hidType` | **1 = omni (point), 3 = spot** |
| `clrColor` | vec3, 0–1 |
| `fIntensity`, `fRadius`, `bEnabled` | |
| `bCastShadow`, `fShadowFactor` | |
| `llgLightGroup` | |
| `fTurnOffFallOff`, `fTurnOffDistance` | |
| `fFlickeringFrequency`, `fFlickeringAmplitude`, `fFlickeringNoise` | campfire flicker |
| `fOuterAngle`, `fInnerAngle` | spots only |

:::caution[Not lights]
`<world>.omnis.fcb` contains **no lights**. "Omni" there means *omnipresent*: world-scope entities
outside the sector grid. Retail `world1` holds five `COmniEntity` DLC Domino hosts; most maps ship a
22-byte empty shell.
:::

### `CProximityTriggerComponent` — the only trigger with geometry

Around 4,000 in `world1`, over half rotated, with meaningful names
(`ProximityTrigger_SafehouseCheck_*`, `W1C3_RE_trigger_Arena`). `vectorSize` is the box; the entity's
`hidAngles.Z` is the yaw in degrees. `CTimeOfDay`, `CDelay` and `CLookAtTriggerComponent` fire on
their own conditions and carry nothing to draw.

`CProximityTriggerComponent::IsInside` is **not** a geometric test — it walks a membership list that
physics maintains, so the box test lives in collider registration.

:::caution[Open]
Whether `vectorSize` is the box's full extent or a half-extent, and whether the box is centred on the
entity, are both unconfirmed — a 2× error either way.
:::

## Consequences for tools

- Resolve archetypes by **case-insensitive fully qualified `hidName`**, keeping the whole chain so
  the shadowed definitions stay inspectable.
- Read every library **through the VFS**, so archive priority, whole-file mod replacement and partial
  FCB fragment overrides are already applied. Game-internal and mod layering then live in one chain.
- Do not resolve a placed entity against `_full` while claiming to model the server, and do not
  resolve it against the base while claiming to model the client.
- Editing an archetype **does** change the 48-in-146 that reference one, and does nothing for the
  rest.

## Unknowns

- What the flag at `+0xC4` selects between the two bases. The obvious write sites were searched and
  none of them is this field.
- Whether Dunia's loop after the patch override is literally `CDlcService::GetEntityLibraries`. In
  `FarCry2_server` it is: `CXGame::LoadArchetypes` (`0x08888750`) calls
  `CDlcService::GetEntityLibraries(CryVector<CryStringBase<char>>&)` and feeds each returned path
  through `CEntityLibraryManager::Override`. Dunia's loop has the same shape — a vector of strings
  walked at `0x1c` stride, each loaded through the same resolver slot and merged the same way — but
  the call itself has not been read there. Either way the DLC libraries land *after* the patch, so
  they win over it.
- Attribute-level precedence inside a merged node: an instance field present in both must win for the
  merge to be useful, but `CReadOnlyMergeNode`'s attribute accessors have not been opened.
