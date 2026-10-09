---
sidebar_position: 16
---

# `CFCSEDataComponent` — the ABI FCSE's entity data is built on

:::info[Verified via reverse engineering]
Every address, slot and offset below was confirmed by decompile against `Dunia.dll` (Steam v1.03),
with `FarCry2_server` for names. Addresses are VAs on **`fc2_103_uplay`**; FCSE resolves the
functions through its address library, so they work on both shipped builds.
:::

:::info[Seen in a running game]
Retail GOG v1.03, 2026-10-08, with FCSE's example plugin: a value authored on
`weapons.Secondary.Makarov` loaded from the entity library, and counts the plugin set on three
weapons were in the save and came back on load — two of those weapons only gained the component at
runtime, so the restore recreated it. `Persist` has not been exercised.
:::

This is the reference behind `tools/FCSE/src/engine/entity_component_abi.h`, which carries the
constants themselves. How the engine creates, saves and restores components in general is in
[the component and property registry](./entity-component-schema.md#adding-a-class-and-what-a-restore-does).

## The class

FCSE registers one class, `CFCSEDataComponent` (class id `0x03314753`, the CRC-32 of its name),
through `CFactory<CEntityComponent>::Register` (`0x10043410`). It does so from the function-registry
provider callback, which runs after `InitDuniaEngine` has registered the engine's own classes and
before any world loads.

Its creator does not construct a component from scratch. It calls the engine's own creator for
`CPersistComponent`, found in the factory's entry array, which allocates `0x18` bytes with the
engine allocator and runs `CEntityComponent`'s constructor. FCSE then points the object at its own
vtable and keeps its store where `CPersistComponent` kept its level. `CPersistComponent` is the
starting point because it is the smallest complete component: two fields of its own, and
`GetWantedEventMask` and `GetUpdateFlags` both return 0, so it is never scheduled or sent events.

| offset | field |
|---|---|
| `+0x00` | primary vtable — FCSE's copy |
| `+0x04` | `IEntityTask` vtable — `CEntityComponent`'s, untouched |
| `+0x08` | entity proxy; the proxy's `+0x0C` is the `CEntity` |
| `+0x0C` | flag byte, 1 from the constructor |
| `+0x10` | FCSE's store, where `CPersistComponent` keeps `selLevel` |
| `+0x14` | unused; `CPersistComponent`'s `OriginalSector` |

## The vtable

Retail's component vtable has **30 slots**, against 33 in the server's GCC build. 30 is measured:
the pointer after slot 29 starts another class's table, stored by that class's constructors. Retail
vtables have no RTTI locator in front of them.

FCSE copies `CPersistComponent`'s table from a throwaway instance and replaces three slots:

| slot | offset | method | FCSE's |
|---|---|---|---|
| 0 | `+0x00` | scalar deleting destructor, `(flags)` | frees the store, then runs the original |
| 1 | `+0x04` | `GetHierarchyInfo()` | FCSE's record |
| 3 | `+0x0C` | `GetDescriptor()` | FCSE's one-member descriptor |

The original destructor releases the entity proxy, resets the object to `CNomadObject`'s vtable and
frees it when bit 0 of `flags` is set.

### What the other slots are

Retail slot `g` is the server's slot `g + 1` up to slot 21, because GCC emits two destructors where
MSVC emits one. Two more virtuals are missing from retail's table: the server's slot 23,
`Update(float, EEntityUpdateFlags)`, and slot 29, `GetParent()`. Both override `IEntityTask`, and
MSVC keeps those only in the interface's own table at `+4`. So retail 22–26 are the server's 24–28,
and retail 27–29 are its 30–32. The same rule gives `CEntity` 13 retail slots against the server's
17.

The names below come from that alignment, measured on the GOG base table (`0x10D88B58`). The
alignment is anchored by the base bodies that match the server's exactly **(RE-verified)**:

| Slot | Server name | Base behaviour |
|---|---|---|
| 4 | `SetEntity` | stores the owner |
| 6 | `OnSpawn` | |
| 7 | `FinalizeLoad` | returns true |
| 8 | `Finalize` | |
| 9 | `CanFinalizeOnLoad` | returns false |
| 10 | `UnloadAsynchData` | |
| 11 | `PreUnload` | |
| 12 | `ShutDown` | |
| 14 | `SetIsVisible(bool)` | |
| 15 | `SetIsActiveInTheEditor(bool)` | |
| 16 | `OnMarkAsGarbage` | |
| 17 | `PreSave` | returns false |
| 18 | `SetupDynamicFromSource` | |
| 19 | `OnEntityMove` | |
| 20 | `GetWantedEventMask` | returns `0xFFFFFF81` |
| 21 | `OnEvent` | returns false |
| 22 | `GetTasks` | adds the component's task when `GetUpdateFlags` matches |
| 23 | `GetUpdateFlags` | returns 1 |
| 24 | `SimulationEnabled` | |
| 25 | `StartOrStopSimulation` | |
| 26 | `DestroySimulation` | |
| 27 | `GetAliasName` | returns `""` |
| 28 | `GetPriorityForUpdate` | returns 5.0 |
| 29 | `SetObjectTypeName(const char*)` | does nothing |

Slots 2, 5 and 13 were not named.

## Class identity

`GetHierarchyInfo` returns `{const char* name; uint32 count; uint32 ids[count]}`: the CRC-32 of every
class from `CNomadObject` down, the class's own last. `CEntity::GetComponent` matches a request
against every id in each component's record, and a component is saved under the CRC-32 of the
record's name unless it has an alias. FCSE's record is `CNomadObject`, `CEntityComponent`,
`CFCSEDataComponent`, the first two copied from `CPersistComponent`'s, so the component is found by
its own id and by no engine class's.

## The descriptor and its member

`GetDescriptor` returns a `CNomadObjectDescriptor`, which is a vector of `CMemberBase*` —
`{members, count, capacity}`. Loading from data calls each member's slot 0; the save path calls
slots 3 and 2. Retail keeps the server's member slot order, every method `__thiscall`:

| slot | method | bytes popped |
|---|---|---|
| 0 | `Load(CNomadObject*, ISerializableNode const*)` | 8 |
| 1 | `Save(CNomadObject const*, ISerializableNode*)` | 8 |
| 2 | `LoadState(CNomadObject*, ISerializableNode const*)` | 8 |
| 3 | `SaveState(CNomadObject const*, ISerializableNode*)` | 8 |
| 4 | `GetDescription` — two arguments | 8 |
| 5 | `GetID(out)` — fills `{name, id}`, returns `out` | 4 |
| 6 | `Set(object, value)` | 8 |
| 7 | `Get(object)` | 4 |

A member is `0x14` bytes: vtable, name, name id, field offset, and an extra word. FCSE's descriptor
holds one member, `Entries`, which reads and writes the component node's children itself. It leaves
out `CEntityComponent`'s own three members, which are written to data only and never loaded or saved
in game.

## The data layout

Each value is a child of the component node, tagged with the CRC-32 of its key, holding exactly one
of `Int` (32-bit), `Float` or `String`:

```xml
<object type="CFCSEDataComponent">
  <object type="example_plugin.Nickname">
    <value name="String" type="String">Old Faithful</value>
  </object>
</object>
```

Children rather than attributes, because a merged archetype-and-instance node answers lookups by id
but reports no attributes ([instancing merges](./entity-instancing.md#attributes-the-instance-wins)).
Tagged by key, because the merge pairs children by tag: an instance's key overrides the archetype's,
and a key only the instance has is added.

`Load` fills the values the data authored; `LoadState` replaces the values set at runtime;
`SaveState` writes only those, so a later edit to the data still reaches an entity in an old save.
`Save` writes both, the runtime ones winning.

## The node interface

Retail's `ISerializableNode` differs from the server's in two ways: one destructor slot instead of
two, and each name's overloads grouped in **reverse** declaration order. The server's 28 `setAttr`
and 29 `getAttr` overloads, `bool` first and `XmlConstNodeRef` last, land at slots 21–48 and 49–77.
The slots FCSE calls, each confirmed against an engine member of that type or against the merge
node:

| slot | offset | call |
|---|---|---|
| 5 | `0x14` | `getChildCount()` |
| 6 | `0x18` | `getChild(unsigned) const` — the overload a merge node implements |
| 10 | `0x28` | `newChild(key)` — returns the new child |
| 13 | `0x34` | `getTag()` — the key, through a hidden return pointer |
| 25 | `0x64` | `setAttr(key, const char*)` |
| 38 | `0x98` | `setAttr(key, float)` |
| 43 | `0xAC` | `setAttr(key, int)` |
| 54 | `0xD8` | `getAttr(key, const char*&)` |
| 67 | `0x10C` | `getAttr(key, float&)` |
| 72 | `0x120` | `getAttr(key, int&)` |

A key (`CSerializationID`) is `{const char* name, uint32 crc}`: binary nodes use the id, XML-backed
ones the name. The engine reads its `unsigned int` properties through the `int` overloads as well.

## Entity calls

| function | address | shape |
|---|---|---|
| `CEntity::GetComponent(CStringID const&)` | `0x104E2CC0` | `__thiscall(entity, const uint32*)`, pops 4 |
| `CEntity::CreateComponent(CStringID const&)` | `0x104E12F0` | the same; factory, then `AddAndInitComponent` |
| `CEntity::Lock` | `0x104DD4F0` | `__thiscall(entity)`; the engine calls it before either |
| `CEntitySystem::ms_instance` | `0x11644E80` | the factory at `+0x5C`: `CreateObject` in slot 1, entries at `+0x20`, count at `+0x24` |

`AddComponent` clears the entity's component lookup cache at `+0x74`, so a component added at
runtime is found by the next lookup.

The word just before it, `+0x70`, holds the entity's state flags. The server keeps the same bits at
`+0x60`. Names are the server's setters and getters; offsets are checked on GOG **(RE-verified)**:

| Bit | Meaning |
|---|---|
| `0x4` | asleep (`SetSleep`) |
| `0x8` | loaded: set by `FinalizeLoad`, cleared by `UnloadAsynchData` |
| `0x10` | marked as garbage, cleared by `Reset` |
| `0x80` | destroyable |
| `0x100` | visible |
| `0x400` | bound |
| `0x800` | should update |
| `0x1000` | initialized |
| `0x2000` | static |
| `0x8000` | from an archetype |
| `0x10000` | being removed |

`GetUpdateFlags` asks for updates only when `(flags & 0x1410) == 0x1000`: initialized, not garbage,
not bound.

## `Persist`

`Persist` gives the entity a `CPersistComponent` through `CreateComponent` if it has none, and raises
its level at `+0x10` to `Full` (2) — what `CTravelDB::PreSaveEntity` does to the entities it carries
between worlds.

## Unknowns

- Whether a `Full` `CPersistComponent` alone gives a world entity nobody has touched a save record.
