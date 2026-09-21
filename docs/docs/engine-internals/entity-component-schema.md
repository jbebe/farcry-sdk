---
sidebar_position: 15
---

# Entities — The Component and Property Registry

:::info[Verified via reverse engineering]
Traced via GhidraMCP against `FarCry2_server`: every `RegisterProperties` body, the member classes'
`Load` methods, `CEntity::CreateComponent` and the `Components` container's loader. The same
property names are present in retail `Dunia.dll` (4,383 of the 4,484 distinct names appear there
verbatim). The dump itself is `tools/fc2re/out/register_properties.jsonl`.
:::

A `.fcb` value carries a name hash and raw bytes, nothing else. What a property is called, what type
it is and which object owns it lives in the engine: every class built on `CNomadObject` registers its
properties once, at start-up, and one generic loader reads every entity, component, config and AI
task through that registration. This page describes that registry, which is where an editor gets
names, types, enum choices and structure for anything in an entity library or sector file.

## How a class declares its properties

`CLASS::RegisterProperties()` allocates one `CMemberBase` per property and pushes it into the class's
`CNomadObjectDescriptor` — or into a group member's own list, which is how nesting is expressed. A
class with a base first calls the base's `RegisterProperties` and copies its members with
`PushBackMembers`, so a descriptor holds the whole inherited list.

| offset | slot |
|---|---|
| `+0x00` | vtable — its template arguments name the member kind, the C++ value type, the type handler and the flag bits |
| `+0x04` | name, a C string |
| `+0x08` | `CStringID` of the name: plain CRC32, case-sensitive — see [entry ids are CRC32](../file-formats/object-inventory.md#entry-ids-are-crc32) |
| `+0x0C` | byte offset of the field in the object; a callback for a serialization event |
| `+0x10` | array index for `COffsetMember`; setter for an accessor member |
| `+0x14` | element tag of a container |
| `+0x18` | getter for an accessor member; predicate for a conditional group |
| `+0x1C` | second element tag |

`FarCry2_server` has 1,049 registrars declaring 5,755 members. 193 classes descend from
`CEntityComponent` (every `CGameObject` — `CPawn`, `CVehicle`, `CWeapon` — is one) and 61 from
`CBaseEntity`.

## Where each kind of member lives in the node

The member kind decides which node the loader reads a property from. Getting this wrong puts a
property in the wrong node, where the engine will never look.

| kind | loads from |
|---|---|
| `CGenericMember`, `CVirtualMember`, `COffsetMember` | a value in the same node |
| `CGroupMember` | a **child node named after the group** (`findChild(name)`), then its members from there |
| `CConditionalGroupMember` | the **same node**, only when its predicate returns true |
| `CContainerMember` | child nodes tagged with the element tag; when the template's last argument is `true` they sit inside a child named after the member, otherwise directly in the node |
| a value typed as a registered class, `GenericTypeHandler<T>` | a child named after the member, loaded with `T`'s own descriptor |
| a value typed as a registered class, `NoChildTypeHandler<T>` | the **same node**, loaded with `T`'s own descriptor |
| `CSerializationEvent` | nothing — a callback run after loading |
| `CEnumMember` | nothing — its `Load` is empty; it only carries labels (below) |

The last two value rows explain a layout that otherwise looks arbitrary.
`CDynamicLightComponent` registers only two properties, `light` and `Enable`, yet a placed light's
component node carries `hidType`, `clrColor`, `fIntensity` and the rest listed in
[Components read off an instance](./entity-instancing.md#components-read-off-an-instance): `light` is a
`NoChildTypeHandler<CSceneObjectHandle<CSceneLight>>`, so `CSceneLight`'s registered properties are
read straight out of the component's own node.

## Flag bits

The last numeric template argument is a bit set of when the member is used:

| bit | meaning | members with it set |
|---|---|---|
| 1 | loaded from data | 4,140 |
| 2 | saved to data | 3,908 |
| 4 | loaded from a savegame | 1,616 |
| 8 | saved to a savegame | 1,559 |
| 16 | has an editor description | 408 |

`3` — data, both directions — is the normal archetype property (3,229 members). `12` is savegame
state only (1,364) and never appears in an entity library or sector file. `2` alone is written by the
editor and read back some other way, such as the root-level `hidSkyOcclusion0`–`3` that
`CGraphicComponent` bakes. Bit 16 is used only by the engine's own config classes
(`CRenderConfig`, `CGameConfig` and 35 others), never by a component.

## Value types

The template's value type decides the wire shape, through the `ISerializableNode::getAttr` overload
the member's `Load` calls. The common cases:

| C++ type | on the wire |
|---|---|
| `bool`, `float`, `int`, `unsigned int`, `unsigned long long`, `EntityId` | the scalar |
| `ndVec_tpl<float,2/3/4>`, `ndAngle3<float>` | 2, 3 or 4 floats |
| `Gear::Quaternion4<float>`, `Matrix44_tpl<float>` | 4 or 16 floats |
| `CryStringBase<char>`, `char const*` | null-terminated string |
| `CStringID`, `CNoCaseStringID`, `CPathID` | a 32-bit hash |
| `CryVector<T>` | count-prefixed array of `T` |
| `unsigned int` with `SoundIDHandler` | a **string**, turned into a sound id on load |
| `unsigned int` with `BasicTypeHandlerEnum` | an index — see below |

### Enums

A choice is a pair. `selXxx` is a plain `unsigned int` index; the `enumXxx` registered right after it
is a `CEnumMember` whose labels are stored into the descriptor at registration, in index order —
`CAIOcclusionVolumeComponent`'s `enumDensity` is `UndefinedDensity`, `LightDensity`, `MediumDensity`,
`HeavyDensity`, `VeryHeavyDensity`. 86 enums carry labels this way. An archetype that ships its own
`enumXxx` child only copies what the registration already says.

### No ranges

Nothing in the registry bounds a value. No member carries a minimum, maximum or step; bit 16's
descriptions exist only on config classes. Neither the shipped data, the prototype builds nor the
community editor source in this repository has any for component properties either — the one
slider system there, in `FC2Editor`, only covers its own brush tools. A range for a component
property has to come from the values the shipped data uses.

## How an entity gets its components

An entity's components are the children of its `Components` node. The container's loader reads each
child's tag as the class id — the CRC32 of the class name — unless the child sets `hidHasAliasName`,
in which case its `hidComponentClassName` string names the class. It then creates the component
through `CFactory<CEntityComponent>`, attaches it to the entity, loads it from the child node and
runs `Init`, then `Finalize` if the entity is already loaded.

176 registered component classes can be created this way. Another 20 have a `CreateObject` but no
`RegisterProperties` at all — `CPawnNetworkComponent`, `CGraphicClusterComponent`,
`CNavMeshGenComponent` and similar network, cluster and runtime-only classes — so there is nothing
of theirs to author.

A placed instance merges over its archetype child by child, pairing by tag
([Instancing merges](./entity-instancing.md#instancing-merges-it-does-not-replace)). So an
instance that adds a component the archetype already has does not add a second one: it overrides the
archetype's.

## What a component needs

No component declares a dependency. `ValidateOtherComponents` is empty on every class that has it,
and nothing checks a component set at load. A component reaches a sibling by calling
`CEntity::GetComponent<T>()` from its own methods; a census of those calls finds 56 component
classes that look one up, for example:

| component | looks up |
|---|---|
| `CAnimationComponent`, `CRigidPhysComponent`, `CStaticPhysComponent`, `CGraphicKitComponent` | `CGraphicComponent` |
| `CGraphicComponent` | `CFileDescriptorComponent` |
| `CFireRealtreeComponent`, `CIgnitorComponent` | `CRealtreeComponent` |
| `CCorpseComponent` | `CCharacterPhysComponent` |
| `CBindingComponent` | `CPhysComponent` |

Treat these as "uses", not "requires". The call does not say which entity it is made on: every
pickup looks up `CPawn`, but on the player that touches it, not on itself. Most callers tolerate a
null result. Which components each archetype actually carries is observable in the shipped data —
see the [component-set census](./entity-instancing.md#a-third-of-a-worlds-entities-draw-nothing).

## What the registry does not name

Most member hashes in a shipped entity library are registered names, but the ones the community
`binary_classes.xml` still leaves unnamed are not: across four shipped libraries the registry names
10 of 61,355 such values. They are keys derived from content, not property names.

## Reproducing this

In `tools/fc2re`: `dump_properties.py` writes the registry dump, `dump_component_uses.py` the
`GetComponent<T>` census and the creatable set, and `build_component_schema.py` turns both into
`tools/JackAll/assets/component_schema.json`, which JackAll merges over `binary_classes.xml`.
