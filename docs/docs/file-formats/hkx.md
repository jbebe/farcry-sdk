---
sidebar_position: 24
---

# `.hkx` — Havok Physics

:::info[Verified via reverse engineering]
The load path is `CRTCollisionResource::LoadCollisionData` → `CPhysResource::ClientProcessRawData`
→ `CPhysResourceImpl::Init` (`0x09b197a0`) in the symbol-bearing `FarCry2_server`. The class
layouts are Dunia.dll's own `hkClass` reflection, and the signatures come from its
`hkClass::getSignature` (GOG `0x10AE8190`). JackAll reads and rewrites all **2,745** shipped
files byte for byte.
:::

An `.hkx` holds a model's collision and physical bodies: rigid bodies, their shapes, and the
constraints between them. It is a 16-byte Dunia prefix followed by a **Havok 5.5.0-r1** binary
packfile in the 32-bit little-endian layout. Every shipped file has the same header.

No public tool edits physics in this version. Skyrim-era tools (hkxcmd, HKXPack, serde-hkx) start
at Havok 2010. HavokLib covers 5.5 but only animation and skeleton classes.

## The prefix

| Offset | Value |
|---|---|
| `+0x00` | `26`, the same value as the packfile's user tag |
| `+0x04`, `+0x08` | Per-file words that are neither a CRC-32 nor a length |
| `+0x0C` | `0xFFFFFFFF` |

`Init` skips all 16 bytes and loads the packfile in place from `+0x10`. It refuses packfile
versions below 4. `CPhysResource` reports the user tag (26) as its version.

## The packfile

| Field | Value |
|---|---|
| Magic | `57E0E057 10C0C010` |
| User tag, file version | 26, 5 |
| Layout rules | `04 01 00 01`: 4-byte pointers, little-endian, no padding reuse, empty-base optimisation |
| Sections | 3: `__classnames__`, `__types__`, `__data__` |
| Contents | `hkRootLevelContainer` at data offset 0 |
| Version string | `Havok-5.5.0-r1`, NUL, `0xFF` |

Each section header is a 19-byte zero-padded tag plus one `0xFF` byte, then seven offsets: start,
local fixups, global fixups, virtual fixups, exports, imports and end.

- **`__classnames__`** lists classes as `u32 signature, 0x09, name, NUL`, padded to 16 with `0xFF`.
  It is not limited to the classes the file instantiates; it lists every class their members and
  parents reach, in an order that cannot be regenerated.
- **`__types__`** is empty except in the seven animal ragdolls, which store full class reflection.
- **`__data__`** holds the objects, then three fixup tables, each padded to 16 with `0xFF`:
  - *local*, 8 bytes: a pointer to data the object owns (array elements or a string)
  - *global*, 12 bytes: a pointer to another object, as (source, section, target)
  - *virtual*, 12 bytes: an object's start and the offset of its class name

The files carry no class reflection of their own, so a reader needs the class layouts.
Dunia.dll builds each `hkClass` at startup through one constructor (GOG `0x10AE7E80`), and those
registrations are the layouts the game loads with: 354 classes. They explain every fixup in every
shipped file. `tools/fc2re/DumpHavokClasses.java` dumps them into JackAll.

### Signatures

A class's signature is a CRC-32 over the class, then each parent in turn. Each class contributes:

- its interface count, its declared enums and the enum count
- for each member: the signature of an embedded struct's class, the member's enum, its name, type,
  subtype, array size and flags
- the member count

Strings are written without terminators. Members Havok does not save are written as type
`ZERO`. Member offsets are not part of the signature, so a signature is the same on every
platform.

### Write order

Havok wrote the data depth-first from the root. Each object is followed by the data it owns, in
slot order and recursively, and then by the objects it points to, also in slot order. Every block
is 16-aligned with zero fill.

- Local fixups are listed in the order their targets were written.
- Global fixups follow slot order, with each array expanded in place. Source order matches only
  where a class's direct pointers come before its arrays; the ragdolls are where it doesn't.
- Virtual fixups are listed in object order.

A variant's class pointer has no fixup unless the file stores `__types__`. Havok still sized the
global table for it, so each such variant leaves one dead `0xFFFFFFFF` entry at the end of the
table.

Array capacity words are `size | 0xC0000000`, or `| 0x80000000` for arrays Havok does not save.
Reference counts are 0.

## What the files hold

The root holds a `"Physics Data"` variant (`hkpPhysicsData`) with one `hkpPhysicsSystem`. In 15
vehicle files it also holds `"NomadPMG"`, a `nomadPartModifiersGroup`: part ids, and the
`hkpRemoveTerminalsMoppModifier`s that cut a broken part's triangles out of the collision mesh.

| Class | Files |
|---|---|
| `hkpRigidBody` | 2,745 |
| `hkpMoppBvTreeShape` over `hkpStorageExtendedMeshShape` | 2,348 |
| `hkpBoxShape` | 2,194 |
| `hkpConvexTranslateShape`, `hkpConvexTransformShape` | 1,714, 1,643 |
| `hkpListShape` | 1,383 |
| `hkpConvexVerticesShape` | 705 |
| `hkpConstraintInstance` | 50 |
| `hkpLimitedHingeConstraintData` | 46 |
| `hkpPositionConstraintMotor` | 40 |
| Ragdolls (`hkaSkeleton`, `hkpRagdollConstraintData`, ...) | 7 |

Car doors, hoods and trunks are separate convex bodies hinged to the chassis by those
constraints, named after the part's damage state (`DoorLeftState02`, `HoodState02`). For each body
`X`, `Init` also records the index of the body named `HIGH_X` (returned by `GetHighresIndex`) and of
the one named `NAV_X`.

**`Init` keeps a position, rotation and mass only for the bodies listed before the first one whose
name contains `HIGH_` or `NAV_`.** These go in a table of `SRigidBodyData`, 0x40 bytes each:
position at `+0x00`, rotation at `+0x10`, mass at `+0x20`. `GetBodyPos` and `GetBodyRotation` index
that table without a bounds check. A body added after the first `HIGH_` body therefore reads past
its end, and a part hinged on it spawns somewhere different on every load. Insert new bodies before
it.

Two members of a body are packed and easy to set wrong:

- **`motion.motionState.maxAngularVelocity` is an `hkUFloat8` byte, not a float.** The doors' 86 is
  13.3 rad/s and 110 is 65.6 rad/s.
- **`material.responseType` 3 is `RESPONSE_NONE`.** `hkpWorld`'s contact-manager setup registers its
  null contact manager for 3 last, against every other response type. So a pair with such a body gets
  no contact forces, even when it sits inside another body's hull.

## Tools

`jackall-cli hkx verify <file.hkx>...` parses and rewrites files and reports any that differ.
`jackall-cli hkx dump <file.hkx>` prints every object, each value under its member path
(`motion.motionState.transform`, `entities[1]`).

In code, `HkxEdit` reads and writes members by those paths. It also reads arrays of pointers and
inserts or appends to them, and `HkxObject.Clone` copies an object. That is enough to add a body and a
constraint to a file; [adding a moving part to a vehicle](../modding/adding-a-moving-vehicle-part.md)
does both. There is no editable text form of a whole file yet.
