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

## MOPP code

An `hkpMoppBvTreeShape` wraps a shape collection (a list, or an extended mesh) with MOPP code: a
byte-coded tree that culls the collection's children by region before any of them is tested. The
game's queries never return a child where the code does not cover it, so a child moved without
rebuilding the code stops colliding where it went. The code lives in an `hkpMoppCode`:

| Member | Holds |
|---|---|
| `info.offset` | xyz: the corner of the grid; w: grid steps per unit |
| `data` | The code bytes |
| `buildType` | 1, built without chunks; no shipped code uses chunks |

The shape's own `moppData`, `moppDataSize` and `codeInfoCopy` are not saved. They are copied from
the code when the file loads.

The grid has 2^24 steps per axis. A coordinate's step is `(x - offset) * scale`. In the shipped
code checked so far (`chairbar01`), the corner is the collection's bounding-box minimum and
`scale = 0xFE0000 / longest side`. A box
query rounds its minimum's step down by one and its maximum's up by one. The code starts on the top
byte grid (`step >> 16`), and a rescale moves it onto finer grids.

The opcodes, as the game's box query (`hkpMoppObbVirtualMachine::queryAabbOnTree`, `0x09e936b0`)
and ray query (`hkpMoppLongRayVirtualMachine::queryRayOnTree`, `0x09e92120`) in `FarCry2_server`
run them:

| Opcode | Bytes | Does |
|---|---|---|
| `00` | 1 | Return |
| `01`-`04` | 4 | Rescale: a grid that many halvings finer, its origin at the three bytes that follow |
| `05`, `06`, `07` | 2, 3, 4 | Jump forward by an 8-, 16- or 24-bit offset |
| `09`, `0A` | 2, 3 | Add an 8- or 16-bit value to the keys of the terminals below |
| `0B` | 5 | Set the value added to the keys of the terminals below |
| `0C` | 3 | Jump to a 512-byte chunk; only chunked builds use it, and none ship |
| `10`-`12` | 4 | Split on x, y or z: the left side if the box's min is below the first byte, the right side (8-bit jump) if its max is above the second |
| `13`-`1C` | 4 | The same split on a diagonal: y±z, x±z, x±y (bytes doubled, minus 255 for a difference), x±y±z (bytes tripled) |
| `20`-`22` | 3 | Split on one value |
| `23`-`25` | 7 | Split as `10`-`12`, with 16-bit jumps to both sides |
| `26`-`28` | 3 | Cut: return unless the box reaches `[lo, hi)` on x, y or z |
| `29`-`2B` | 7 | Cut on 24-bit top-grid steps |
| `30`-`4F` | 1 | Terminal: key 0-31 |
| `50`-`53` | 2-5 | Terminal: an 8-, 16-, 24- or 32-bit key |
| `60`-`6B` | 2-5 | Set a property; the box query skips it |

A terminal's key names one child of the collection:

- In a list, it is the child's index.
- In an extended mesh, it is `subpart << (32 - numBitsForSubpartIndex) | index`, with the top bit
  set for a shapes subpart. Degenerate triangles have no key. These are triangles whose doubled area
  squared is below `1e-7`, by `hkpTriangleUtil::isDegenerate` (`0x09e077d0`).

The game also builds MOPP code itself. `SetupMopp` wraps any list of more than five shapes that
`CreateShapeFromArray` assembles at runtime, through Havok's own `hkpMoppUtility::buildCode`.

## Tools

`jackall-cli hkx` makes a collision edit without the app:

| Command | What it does |
|---|---|
| `tree <file>` | Prints the bodies, shapes and constraints, each by its number `#n`, and where each shape sits in the model |
| `move <file> <#n> --by x,y,z` (or `--to`) | Moves a shape in the model, Z up |
| `turn <file> <#n> --degrees d [--axis z]` | Turns a shape about a model axis through where it sits |
| `resize <file> <#n> --size x,y,z` (or `--face +z --by d`) | Resizes a box about its centre, or moves one face and keeps the opposite one |
| `add-box <file> <#n> --size x,y,z --at x,y,z` | Adds a box to the list or extended mesh group holding shape `#n`, with that shape's material and collision filter |
| `remove <file> <#n>` | Takes a shape out of its list or extended mesh group |
| `decode <file>`, `encode <xml> --original <file>` | The whole file as XML and back: every object by number, every saved member by path |
| `refresh <file> --original <file>` | Brings an edited file's saved values in line with its shapes, as [below](#what-a-save-has-to-refresh) |
| `check <file>...` | Reports what would break in game: a body after the first `HIGH_` or `NAV_` one, a constraint joining a body from another system, list bounds short of the children, MOPP code that no longer finds a shape, and stale copies |
| `dump <file>` | Prints every object, each value under its member path (`motion.motionState.transform`, `entities[1]`) |
| `verify <file>...` | Parses and rewrites files and reports any that differ |

`#n` names a convex shape or the translate or transform shape around it. The edit commands write in
place unless given `-o`. Each one refreshes what it changed, runs `check`, and writes nothing if the
check finds an error. An edit that adds or removes objects renumbers them, so run `tree` again after
one. `encode` refreshes the same way against `--original`.

`check` finds nothing on any of the 2,745 shipped files, and all of them decode and encode back byte
for byte (`verify --xml`). With `--mopp`, `verify` also runs every MOPP's code and checks two
things: that the code reaches exactly its collection's children, and that code rebuilt from those
children keeps every child a box touches. All 4,547 shipped MOPPs over lists and extended meshes
rebuild that way. In nine shipped mesh MOPPs, Havok's code leaves out or keeps one or two sliver
triangles that sit right at the degeneracy limit, where the two float roundings disagree. Two MOPPs
over `hkpSimpleMeshShape` are not rebuilt.

In code, `HkxEdit` reads and writes members by those paths. It also inserts, appends and removes
array elements, and `HkxObject.Clone` copies an object. That is enough to add a body and a
constraint to a file; [adding a moving part to a vehicle](../modding/adding-a-moving-vehicle-part.md)
does both. `HkxShapeEdit` places, resizes, adds and removes shapes, `HkxCaches.Refresh` refreshes a
file against its original, and `HkxLint` is `check`. `HkxMopp` runs MOPP code as the game's box query
does, and `HkxMopp.Rebuild` rewrites a MOPP's code from where its children are now. The code it
writes uses only rescales, axis splits, cuts and terminals, and comes out about twice the size of
Havok's.

In the JackAll app, **Open in HKX Editor…** on the Files tab draws a file's collision over the model
filed beside it, with boxes and hulls grown by their convex radius. Shapes can be edited there: their
values, where they sit, and a box's size.

### What a save has to refresh

Havok works some values out from the shapes once and saves them, and the game reads them back as
saved:

| Value | Worked out by | From |
|---|---|---|
| A list's `aabbHalfExtents`, `aabbCenter` | `hkpListShape::recalcAabbExtents` (`0x09de8a30`) | The union of its children's boxes |
| An extended mesh's bounds | `hkpExtendedMeshShape::recalcAabbExtents` (`0x09dee1f0`) | Triangles grown by `triangleRadius`, shapes placed by their subpart |
| A body's `motion.motionState.objectRadius` | `hkpRigidBody::updateCachedShapeInfo` (`0x09d4e030`) | Distance from the centre of mass to the centre of the shape's box, plus half the box's diagonal |
| A MOPP's code | (see above) | Its collection's children |
| A translate or transform shape's `radius` | The exporter | Its child's radius: all 28,709 shipped ones match exactly |
| A list child's `numChildShapes` | The exporter | The list's child count: all 20,567 shipped ones match |
| A storage mesh's `shapestorage[k].shapes` | The exporter | Its `shapesSubparts[k].childShapes`, in order: all 205 shipped ones match |

Which of a storage mesh's two shape arrays the game reads has not been traced; JackAll keeps them
equal. Loading does not recompute the other values; `finishLoadedObject` only sets vtables. The shipped radii
and list bounds of bodies with hulls are often larger than the formula gives, by about the hulls'
convex radius. The exporter probably measured them before shrinking the hulls. So JackAll does not
recompute these values from scratch. It moves each one from the original file's value by how much
the shapes' own value changed, and an unedited file saves back byte for byte.
