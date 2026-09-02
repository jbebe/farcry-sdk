---
sidebar_position: 20
---

# `movemgr.bin` — MOVE animation graph

:::info[Verified via reverse engineering — first documentation of this format]
No community write-up, tool, or prior note covers this format, and nothing on this site referenced
it before. Everything below was traced live through GhidraMCP against the symbolized
`FarCry2_server` binary and then checked byte-for-byte against the shipped files in
`tmp/gamefiles`. Claims that are measured rather than read out of the disassembly say so.
See [intro](../intro.md) for how RE-verified and community-reported claims are distinguished.
:::

MOVE is Far Cry 2's animation state graph — the layer that decides *which* `.mab` clip plays for a
given character situation. `.mab` is the clip, `.skeleton` is the rig, and MOVE is the machine that
picks between them. It is what turns "the player is crouched, in iron sights, holding an AK-47" into
a specific animation blend.

Four instances ship:

| File | Bytes | Role |
|---|---|---|
| `common/graphics/move/movemgr.bin` | 1,858,293 | the base graph, loaded at startup |
| `common/graphics/move/movemgrnamed.bin` | 3,600,120 | same graph with names embedded |
| `downloadcontent/dlc1/entitylibrary/graphics/move/dlc1.bin` | 473,404 | DLC1's expansion package |
| `downloadcontent/dlc1/entitylibrary/graphics/move/dlc1named.bin` | 878,300 | the named twin of the above |

## How it's loaded

### The base graph

`MSAnim::LoadMoves` (`0x09b98700`) contains no path literal — it reads one from the engine config:

```c
const char* path = CConfig::Get("files", "Move File");
CFileSimpleStream stream(path, 0, true);
CMoveMgr::CreateFromStream(&stream);
if (CMoveValueContainer::ms_iNumMoveValue != 0x69)  // 105
    discard;
```

The key resolves in `common/config/defaultengineconfig.xml:25`:

```xml
<file id="Move File" string="graphics/Move/MoveMgr.bin"/>
```

That `ms_iNumMoveValue != 0x69` check is load-bearing and is the hardest constraint in the format:
**the engine accepts a MOVE file only if it declares exactly 105 value channels.** The count is not
read from data and reconciled — it is compared against a hardcoded 105 and the file is dropped
otherwise.

`CMoveMgr::CreateFromStream` (`0x09b5ba30`):

```c
MoveMgrHeader hdr;                                  // { u32 m_type; u32 m_version; }
MoveMgrHeader::Serialize(&hdr, stream);
if (!hdr.IsSameVersion(MoveMgrHeader::DEFAULT)) bail;
stream->Transfer("dwFileFormat", &dwFileFormat, 4); // u32
if (dwFileFormat & 0x20000) bail;                   // <- rejects the *named* variant
recreate the serial stream with dwFileFormat as its feature flags
stream->TransferPointer<CMoveMgr>(nullptr, "CMoveMgr", &root);
```

### The `named` twins are not loadable

`dwFileFormat` is the serializer's feature-flag word, handed to `CBinaryReadSerialStream`. Measured
across all four shipped files:

| File | `dwFileFormat` |
|---|---|
| `movemgr.bin`, `dlc1.bin` | `0x00050000` |
| `movemgrnamed.bin`, `dlc1named.bin` | `0x00070000` |

Bit `0x20000` is the only difference, and `CreateFromStream` explicitly refuses any file that sets
it. So **the engine loads `movemgr.bin`; `movemgrnamed.bin` is an authoring artifact the primary
loader is coded to reject.** The named variant is nonetheless the one worth reading — it carries
every channel, state and clip name as inline text, which is how every layout below was recovered.

`CMoveMgr::MergeMoveExpansion`, the DLC path below, does *not* carry the `0x20000` check.

### Expansion packages

`CDlcService::Mount` (`0x09232770`) walks every installed DLC and, for each one whose `CDlcInfo`
declares a moves file and matches the current game mode, calls `MSAnim::AddMovesFile` →
`CMoveMgr::MergeMoveExpansion` (`0x09b5bb80`):

```c
read header + dwFileFormat exactly as above
stream->TransferPointer<CMoveObject>(nullptr, "CMoveObject", &root);   // a CMoveStateMachine
for (i = 0; i < root->stateCount; i++) {
    state = root->GetMoveState(i);
    globalStateMachine->AddMoveState(state);
    parent = state->m_parentStringID;                 // CPathID, 0xFFFFFFFF = none
    if (parent == 0xFFFFFFFF) continue;
    id = globalStateMachine->GetStateIDFromStringID(parent);
    if (id < 0) continue;
    globalStateMachine->GetMoveState(id)->InsertExpansionMove(state);
    res = CResourceManager::GetExistingResource(parent)
       ?: CResourceManager::CreateResource(parent, "CMovementResource");
    res->GenerateChildren();
}
```

The semantics matter: **an expansion grafts new states onto existing states, matched by name.** Each
incoming state names a parent that must already exist in the base graph, and a state whose parent
cannot be resolved is added to the machine but attached to nothing. Nothing else in the file merges.

## File layout

### Header

```
offset  size  field
0       4     m_type       — 'M','V','M','\0'  (0x004D564D)
4       4     m_version    — 5
8       4     dwFileFormat — serializer feature flags (0x50000, or 0x70000 when named)
```

Both `m_type` and `m_version` must equal `MoveMgrHeader::DEFAULT` exactly; `IsSameVersion` is a
plain two-field comparison.

### The serializer grammar

Everything after the header is a graph of polymorphic objects written through the engine's generic
`ISerialStream`. The binary reader (`CBinaryReadSerialStream`, `0x09ba85c0`–`0x09ba94e0`) is far
simpler than the field names in the disassembly suggest: **no field name, type tag or length prefix
is written for scalars.** Every primitive is raw little-endian bytes at the current position, and the
`char const*` name passed to each call is debug-only.

| Primitive | Reads |
|---|---|
| `TransferSignedValue(name, p, n)` | `n` raw bytes |
| `TransferUnsignedValue(name, p, n)` | `n` raw bytes |
| `TransferDecimal(name, p, n)` | `n` raw bytes (IEEE float) |
| `TransferString(name, p, max)` | `u32 len` then `len` bytes — **no NUL in the stream** |
| `TransferData(name, p, max)` | `u32 len` then `len` bytes |
| `TransferVersion(&v)` | `u32 tag`; if it is `0x3ADE68B1`, `u32 v` follows, else seek back 4 and `v = 0` |
| `TransferNamedEnum<E>(...)` | `u32` — the name table is used only by the text serializer |

`TransferVersion` being self-describing is what lets one reader handle several file revisions: a
`0x3ADE68B1` tag is a version stamp, anything else is the next field.

`BeginTransferComplex(fieldName, typeName, &ptr)` opens an object:

```c
if (ptr == NULL) return 0x222;                 // scoping marker only — reads NOTHING
if (flags & 0x800000) { u32 h; if (h != hash(fieldName)) fail; }   // not set in any shipped file
s32 idx = read4();
if (idx == -2) { *ptr = NULL; return 0; }      // null pointer
if (idx == -1) return 0x333;                   // new object
*ptr = map[idx];  return 0x555;                // back-reference to an already-read object
```

The `0x333` path then reads a `u32 ClassType`, allocates via
`CGlobalSerialInfo::Allocate(ClassType, 'MvOb')`, registers the instance in the back-reference map,
and calls the object's own `Serialize`. `'MvOb'` (`0x4D764F62`) is the factory category constant for
the whole MOVE subsystem. `0x111` and `0x222` are write-side and in-place-reread codes that the
binary read path never returns.

The crucial consequence for anyone writing a parser: **most `BeginTransferComplex` calls in the MOVE
code pass a null pointer and therefore consume no bytes at all.** Names like `"DefinitionFile"`,
`"PackageList"`, `"MoveBlendSet"` and `"CMoveValue"` are pure scoping markers for the text/XML
serializer; in the binary file they are invisible. Only the calls made through `TransferPointer<T>`
read anything.

### The three idioms every record is built from

Above the primitives, the whole format is those primitives arranged by three recurring patterns.
Learn these and the [record catalogue](#every-record-in-full) reads as a formality.

**Inheritance is inlined, and not always at the front.** A derived class serializes its base by
calling the base's `Serialize` directly, so the base's fields land *inline* in the stream at the
point of the call — there is no separate base record and no marker around it (the
`BeginTransferComplex("Parent CMoveObject", …)` that wraps such calls passes a null pointer and
writes nothing). The trap is that **the call site is not always first**:

```
CMoveGroup            CMoveDescriptorGroup ; ver ; u8              <- base first
CMoveDefParameter     ver ; f32 x3 ; u32 ; f32 x2 ; CBaseAnimGroup ; u8 …   <- six fields, THEN base
CMoveBaseState        ver ; CMoveDescriptorGroup ; u32 ; u32       <- base in the middle
```

`CMoveDefParameter` reads its own version and six scalars before descending into `CBaseAnimGroup`,
which itself descends into `CMoveDescriptorGroup` → `CMoveDescriptor` → `CMoveObject`. A single
`CMoveDefParameter` record is therefore five classes deep, with all five contributing interleaved
fields, and it then resumes with fifteen more of its own (at the shipped version 25) once the base
returns.

**Lists are null-terminated pointer chains, never counted.** The engine holds these as linked lists
and serializes them by walking the chain, so the file has no length prefix:

```c
ptr = head;
while (true) {
    TransferPointer<T>(nullptr, "T", &ptr);
    if (ptr == nullptr) break;      // the -2 in the stream ends the run
    ...
}
```

Every list is a run of pointer words closed by `-2`. An empty list is exactly four bytes of
`FE FF FF FF`, and a `CMoveDescriptorGroup` with all three of its lists empty — very common for leaf
records — shows up as twelve bytes of `FE`s in a row. Only two pointer runs in the whole format are
counted instead of terminated, and both belong to structures that are fixed-size by nature: the
state machine's `u32 nbState`, and `CMoveMgr`'s transition matrix, which is `ncat × ncat` pointers
sized by the blend-category count read much earlier.

**Every class carries its own version.** `TransferVersion` is called by most `Serialize` methods
with that class's current version as the default, and the reader branches on whatever it finds. The
versions are independent — a `CAnimTech` at version 9 sits inside a `CBaseAnimGroup` at version 9
inside a `CMoveDescriptorGroup` at version 2 — which is why the record layouts are dense with
`[v>N]` gates. A writer must re-emit the version it read, not its own idea of current, or every gate
below it shifts.

### `dwFileFormat` — the feature flags

`dwFileFormat` is handed to `CreateBinarySerialStream` as the stream's flag word, and the shipped
files use only three bits. Recovered by finding every `GetFlags()` test reachable from the MOVE
serializers:

| Bit | Meaning |
|---|---|
| `0x10000` | serialize *definitions* (the schema). When clear, `CMoveValueContainer` serializes live channel **values** instead — the savegame/network path |
| `0x20000` | **named** — authoring build. `CreateFromStream` rejects it outright |
| `0x40000` | include the state graph, the transition matrix and criteria payloads |
| `0x800000` | prefix every complex with a `u32` hash of its field name (`h = 0x6f; h = c + h*0x71`) |
| `0x1000000` | byte-swap every scalar — the big-endian Xbox 360 path, set by `CreateBinarySerialStream`'s last argument, not stored in the file |

Both shipped variants set `0x10000 | 0x40000`; the named twins add `0x20000`. No shipped file sets
`0x800000`, so no field-name hashes appear in practice.

### Class IDs

Every serializable class returns a FourCC from `GetSerializationClassID()`. Because they are stored
as little-endian `u32`, they read **reversed** in a hex dump — `CMoveMgr`'s `'MvMg'` appears on disk
as `gMvM`. All 47 constants, recovered by decompiling every `GetSerializationClassID` in the binary:

| u32 | On disk | Class |
|---|---|---|
| `4D764D67` | `gMvM` | `CMoveMgr` |
| `4D76534D` | `MSvM` | `CMoveStateMachine` |
| `4D765643` | `CVvM` | `CMoveValueContainer` |
| `4D765664` | `dVvM` | `CMoveValueDef` |
| `4D764253` | `SBvM` | `CMoveBaseState` |
| `4D765354` | `TSvM` | `CMoveState` |
| `4D765379` | `ySvM` | `CSyncState` |
| `4D76444E` | `NDvM` | `CDoNothing` |
| `4D764466` | `fDvM` | `CMoveDefinition` |
| `4D764772` | `rGvM` | `CMoveGroup` |
| `4D436D74` | `tmCM` | `CMoveComment` |
| `4D537452` | `RtSM` | `CMoveStateRef` |
| `4C537452` | `RtSL` | `CLayeredStateRef` |
| `4C795354` | `TSyL` | `CLayeredState` |
| `4C794178` | `xAyL` | `CLayeredAxialBlend` |
| `4C795061` | `aPyL` | `CLayeredParameter` |
| `506C4D53` | `SMlP` | `CPlayerMoveState` |
| `466B5354` | `TSkF` | `CFrankensteinState` |
| `466B5061` | `aPkF` | `CFrankensteinParameter` |
| `42534147` | `GASB` | `CAxialBlendAnimGroup` |
| `41416E63` | `cnAA` | `CAnimTechAnchor` |
| `41744174` | `tAtA` | `CAnimTechAttach` |
| `4174494B` | `KItA` | `CAnimTechIKPath` |
| `4174506F` | `oPtA` | `CAnimTechPossession` |
| `41526167` | `gaRA` | `CAnimTechRagdoll` |
| `416E5061` | `aPnA` | `CMoveDefParameter` |
| `53794465` | `eDyS` | `CSyncDefinition` |
| `53795061` | `aPyS` | `CSyncDefParameter` |
| `54434C70` | `plCT` | `CTimeControlledLayeredParameter` |
| `54434D70` | `pmCT` | `CTimeControlledMoveParameter` |
| `4E494C73` | `sLIN` | `CNotInterruptibleLink` |
| `544C4173` | `sALT` | `CTransitionLink` |
| `4D454944` | `DIEM` | `CMoveCriteriaEntityIDEqual` |
| `43494E45` | `ENIC` | `CMoveCriteriaEntityIDNotEqual` |
| `4D434545` | `EECM` | `CMoveCriteriaEnumEqual` |
| `43454E45` | `ENEC` | `CMoveCriteriaEnumNotEqual` |
| `4D455543` | `CUEM` | `TMoveCriteriaEqual<uint8>` |
| `4D4E4543` | `CENM` | `TMoveCriteriaNotEqual<uint8>` |
| `4D634549` | `IEcM` | `TMoveCriteriaEqual<int>` |
| `4D4E4549` | `IENM` | `TMoveCriteriaNotEqual<int>` |
| `4D634542` | `BEcM` | `TMoveCriteriaEqual<bool>` |
| `4D4E4542` | `BENM` | `TMoveCriteriaNotEqual<bool>` |
| `4D634949` | `IIcM` | `TMoveCriteriaIntv<int>` |
| `4D634946` | `FIcM` | `TMoveCriteriaIntv<float>` |
| `4D634941` | `AIcM` | `TMoveCriteriaIntv<CAngle>` |
| `4D635049` | `IPcM` | `TMoveCriteriaPerc<int>` |
| `4D635046` | `FPcM` | `TMoveCriteriaPerc<float>` |

The shape of the graph is legible from a census of those byte signatures — naive substring counts
over the two named files, so treat them as close rather than exact:

| Class | `movemgrnamed.bin` | `dlc1named.bin` |
|---|---|---|
| `CMoveMgr` | 1 | **0** |
| `CMoveStateMachine` | 1 | 1 |
| `CMoveValueContainer` | 1 | **0** |
| `CMoveDefParameter` | 1,656 | 379 |
| `CMoveGroup` | 1,594 | 494 |
| `CAnimTechAnchor` | 857 | 93 |
| `CAnimTechIKPath` | 830 | 368 |
| `CLayeredParameter` | 718 | 253 |
| `CTransitionLink` | 662 | 159 |
| `CMoveState` | 572 | 16 |
| `CLayeredAxialBlend` | 371 | 232 |
| `CLayeredState` | 155 | 18 |
| `CAxialBlendAnimGroup` | 132 | 48 |
| `CMoveComment` | 51 | 13 |

An expansion is a bare `CMoveStateMachine` — no manager, and **no value container at all**.

### Top-level layout

`CMoveMgr::Serialize` and its five helpers give the complete outer structure. Written as a reader,
with every byte the binary path actually consumes:

```
u32   m_type = 'MVM\0'
u32   m_version = 5
u32   dwFileFormat                       ← stream is recreated with this as its flags
s32   -1                                 ← TransferPointer<CMoveMgr>: new object
u32   ClassType = 'MvMg'
  ver   CMoveMgr version            (5)  ← everything below is gated on flags & 0x10000
  ver   CMoveObject version         (1)
  ── SerializeDefinitionFile ────────────────────────────  (version > 4 only)
  ver   DefinitionFile version      (5)
  s32   -1 ; u32 ClassType = 'MvVC'      ← TransferPointer<CMoveValueContainer>
    u32   ms_iNumMoveValue          (105)
    ver   CMoveObject version       (1)
    105 × { u32 m_eMVType ; u8 m_fMirrorable }
  (EntityList — a scoping marker, zero bytes)
  ver   PackageList version         (1)
  u32   package count               (126)
  126 × { str Name ; str Extension ; str ExportWithWorld }
  ── SerializeTransitionFile ────────────────────────────  (version > 4 only)
  ver   TransitionFile version      (5)
  s32   m_iNumMoveBlendSet          (1)
  per set:  s32 categoryCount (16)
            per category: s32 parentCount        (version > 3 only)
                          s32 poseCount
                          u8  stationary
                          poseCount × s32 mirrorPose
  s32   -1 ; u32 ClassType = 'MSvM'      ← TransferPointer<CMoveStateMachine>
    ver   CMoveObject version       (1)
    u32   nbState                   (1700)       (flags & 0x40000)
    1700 × TransferPointer<CMoveBaseState>
  TransferPointer<CMoveStateRef>         ← transition matrix default
  16 × 16 × TransferPointer<CMoveStateRef>
```

`ver` is a `TransferVersion` pair: `u32 0x3ADE68B1` followed by `u32 version`. Walking `movemgr.bin`
with exactly these rules reaches the channel table at `0x40`, ends it at `0x24D`, ends the package
list at `0x16B4`, ends the blend sets at `0x1774`, and lands on `FF FF FF FF 'MSvM'` — the state
machine, declaring **1,700 states**. No drift anywhere, which is what validates the whole chain.

Two incidental findings from that walk:

- The 126 animation-package names are the same identifiers `depload.dat` uses
  (`ak47`, `dart_rifle`, `6p9`, `mgl140`, `turret_browning`, `buddyrescue_*`, `pkg_a1lm01_se01`…).
  `dlc1_crossbow` is **already present in the base `movemgr.bin`**, while `dlc1_sawedoff_shotgun`
  and `dlc1_silenced_shotgun` are not — the crossbow alone was wired into the shipping graph.
- Every package's `Extension` field holds the same 11 non-text bytes
  (`FF E9 90 7C F6 0D 81 7C E8 5F 01`), 126 times over. `CMoveMgr::SerializePackage` fills `Name`
  and `ExportWithWorld` from the object but writes `Extension` straight out of an uninitialised
  stack buffer, so the field is exporter garbage. Read its length, ignore its content.

### The value-channel table

The single `CMoveValueContainer` is the graph's blackboard: a fixed list of channels the game writes
each frame and that transition criteria read. It is indexed purely by position.

:::caution[The loadable file carries no channel names]
In `movemgr.bin` a channel is **five bytes and nothing else** — `u32 m_eMVType`, `u8 m_fMirrorable`.
There is no name, no enum value list, and no string data anywhere in the file: grepping
`movemgr.bin` for `EquippedWeapon`, `IronSight` or `SawedOffShotgun` returns **zero** hits, and the
`ms_rgMoveValueDef` array the table loads into (`0x0a5492a8`, stride `0x10`) sits in zeroed `.bss`.
Channel names and enum value names exist **only** in `movemgrnamed.bin`. See
[the weapon ceiling](#equippedweapon--the-weapon-ceiling) for why this matters.
:::

So there are two record shapes. In `movemgr.bin`, from `0x40`:

```
0  4  m_eMVType
4  1  m_fMirrorable
```

In `movemgrnamed.bin`, from `0x7D`, the authoring build adds the names inline:

```
offset  size      field
0       4         m_eMVType
4       1         m_fMirrorable
5       4         nameLen
9       nameLen   name
        —— only when m_eMVType == 5 (Enum) ——
        4         valueCount
        4         valueCount (repeated)
        …         valueCount × { u32 len; char[len] value }
```

`EMoveValType` comes from the switch in `CMoveValueContainer::Serialize` (`0x09b70da0`):

| Value | Type | Transferred as |
|---|---|---|
| 1 | int | 4 bytes |
| 2 | float | 4 bytes |
| 3 | bool | 1 byte |
| 4 | Angle | `ISerialStream::TransferAngle` |
| 5 | Enum | 4 bytes |
| 6 | uint8 | 1 byte |
| 7 | EntityID | 1 byte |

Walking the named shape from `0x7D` consumes exactly 105 channels and lands on `0x14B4`; walking the
plain shape from `0x40` consumes 105 and lands on `0x24D`. Both files agree on every one of the 105
`{type, mirrorable}` pairs, which is what confirms both layouts at once.

Channels are indexed in file order, and **the index is the only channel identity the engine has.**
It is a `u8` everywhere — `GetValueType(uchar)`, `GetValueNameFromID(uchar)`, and the `m_eValueID`
field of every criterion. `CMoveValueContainer::GetValueIDFromName` maps a `CStringID` to the index
through an `ldiv`-based scramble on `0x1F31D`, the same non-CRC32 hash the
[Magma menu system](../engine-internals/magma-menu-system.md) uses for its page table — a
*precomputed* string ID, never a text name.

The counterpart `GetValueNameFromID` (`0x09b70840`) makes the point unmistakable. In the shipped
build its entire body is:

```c
return (id == 0xFF) ? "VALUE_NONE" : "INVALID VALUENAME";
```

The release engine cannot name a channel because it never had the names. They were compiled away
into `CStringID` hashes, and the only place the text survives is `movemgrnamed.bin`.

A representative slice of the 105; the full list is reproducible with the record shape above:

```
  0 Angle    HeadingAngle              17 Enum   EquippedWeapon (44 values)
  1 Angle    FacingAngle               18 Enum   DesiredWeapon  (44 values)
  2 float    Speed                     19 Enum   Vehicle (12)
  3 float    Acceleration              20 Enum   Stim (15)
  4 bool     Mirror   (mirrorable)     21 bool   Jammed
  7 Enum     AimStance (None, Normal, IronSight)
  8 Enum     Stance    (Standing, Crouched, Swimming, HeadDown, ChestDown)
 13 Enum     CameraPlacement (FirstPerson, ThirdPerson)
 31 Enum     CoverStance (None, LeaningLeft, LeaningRight, LeaningUp, Blindfiring)
 64 Enum     HitLocation (12)
 90 Enum     DeadBy (None, Bullet, Shotgun, Grenade, Fire)
```

Exactly three channels carry `m_fMirrorable`: **4 `Mirror`, 21 `Jammed` and 54
`LadderActualStepEven`**. All three are `bool`, which is the semantic check that the flag byte was
read in the right position.

### How criteria reference a channel

`CMoveCriteria::Serialize` (`0x09badb50`) and `CMoveCriteriaEnumEqual::Serialize` (`0x09b72df0`)
give the encoding every transition test shares:

```
ver   CMoveCriteriaEnumEqual version   (1)
s32   m_Value                                 ← the compared value          (flags & 0x40000)
  ver   CMoveCriteria version          (4)
  s8    m_eValueID                            ← the channel index, ONE byte (flags & 0x40000)
  ver   CMoveObject version
  u8    m_bHysteresisEnabled                  (version < 4 only)
  s32   m_logicOperator                       (version > 2 only)
```

So a criterion is *"channel `m_eValueID` equals the integer `m_Value`"* — one byte of channel index
and a full signed 32-bit comparand. Nothing consults a value count, a name, or an enum table at load
time or at evaluation time. The `TMoveCriteria*` template family (`Equal`, `NotEqual`, `Intv`,
`Perc` over `int`/`float`/`bool`/`uint8`/`CAngle`) differs only in the width and type of `m_Value`.

### State identity

`CMoveBaseState::Serialize` (`0x09b78c60`) is where a state gets its name, and it explains why the
named twins are structurally unreadable rather than merely rejected:

```
ver   CMoveBaseState version   (5)
  CMoveDescriptorGroup::Serialize
u32   m_stateNameHash                    (version >= 4)
u32   aliasID                            (version > 4)
```

The name is stored **only as a `CPathID` hash**, and on the read side the flag word decides how it
is interpreted:

```c
if ((flags & 0x20000) == 0) m_pathID = CPathID(hash);            // normal
else                        m_pathID = CPathID("*** UNSUPPORTED ***");
```

A named file's state names are text the shipped build has no parser for, so it substitutes a literal
placeholder. That is a second, independent reason `0x20000` files cannot be loaded — clearing the
bit in the header would not help, because the state records genuinely differ.

## Every record, in full

The whole format is decoded. A reader built from the layouts below consumes **`movemgr.bin`
exactly — 1,858,293 of 1,858,293 bytes, 22,117 objects — and `dlc1.bin` exactly — 473,404 of
473,404 bytes, 5,724 objects — with zero drift and no unknown class IDs**, and a writer re-emits
both byte-for-byte (see [Writing MOVE files](#writing-move-files)). Because every object begins with
a pointer word and a FourCC drawn from a fixed set of 47, a single mis-sized field derails the walk
within a few hundred bytes; consuming two whole files to the last byte is the proof that none of
these layouts is wrong.

Notation: `ver` is a `TransferVersion` pair (absent tag ⇒ version 0); `str` is `u32 len` + bytes;
`data` is the same shape; `sid` is a `CStringID`/`CPathID` — `u32 hash`, plus a source `str` **only**
in named files; `hname` is a `CHashName` — `u32 hash` + `str`, always; `ptr` is the pointer word
(`-1` new, `-2` null, `>= 0` back-reference); `list` is a run of `ptr`s terminated by a `-2`.
`[G]` marks a field present only when `dwFileFormat & 0x40000`.

### The inheritance spine

```
CMoveObject             ver
CMoveDescriptor         CMoveObject ; list<CMoveCriteria>
CMoveDescriptorGroup    CMoveDescriptor ; ver v ; list ; [v>0] list ; [v>=2] list
CMoveBaseState          ver v ; CMoveDescriptorGroup ; [v>=4] u32 stateNameHash
                        [v>4] u32 aliasID          <- CPathID of the PARENT state; see "Naming a state"
CBaseAnimGroup          CMoveDescriptorGroup ; ver v ; [v>=1] f32 animGroupValue
                        [v>=3] list<CAnimTech> ; [v>3] f32 headLookAtEnable
                        v in {5,6} ? list<CTransitionLink>
                                   : ([v>=6] u8 livePosture ; [v==8] u8 ; [v>8] s32 weaponOffsetMode ; u8 destructiveLookat)
CAnimTech               ver v ; CMoveObject ; [v<9] f32,f32,s32
                        f32 startIn, f32 durIn, f32 startOut, f32 durOut ; u32 blendIn, u32 blendOut
                        (1<=v<=5) ? s32,s32,s32 : s32 parentID
                        [v<9] s32 + hname   |   [v>=9] sid modelHashNamePart
                        [v>1] str partName ; [v>2] hname parentBoneName
CMoveCriteria           ver v ; [G] u8 valueID ; CMoveObject ; [v<4] u8 hysteresis ; [v>2] s32 logicOperator
CMoveDefParameter       ver v ; f32 start, f32 stop, f32 cut ; u32 blendType ; f32 blendTime, f32 multiplier
                        CBaseAnimGroup ; u8 interruptible ; [v>24] u8 dropEvents ; [v<19] f32 physicsEnable
                        f32 muscleIntensity ; s32 loopOverride ; u8 categoryOverride ; s32 cutBehaviour
                        u8 motionOrientCorrection ; f32 lastAnimDuration ; [v>15] u32 animNameHash
                        [v>16] u8 bodyPartAvail ; [v>19] u8 lowerBodyProgress ; [v==18] u8
                        [v>18] u8 ragdollController ; [v>20] u8 displacementMode
                        [v>21] sid package ; [v>22] u8 poseInfoForPMS
```

### Everything else

```
CMoveMgr                see "Top-level layout" above
CMoveStateMachine       CMoveObject ; [G] u32 nbState ; nbState x ptr<CMoveBaseState>
CMoveValueContainer     u32 count ; CMoveObject ; count x (u32 type, u8 mirrorable)
CMoveValueDef           s32 type ; u8 mirrorable
CPlayerMoveState        = CMoveValueContainer

CMoveState              ver v ; [v<=1] u8,u8,u8 ; CMoveBaseState
CLayeredState           = CMoveBaseState        CSyncState = CMoveBaseState
CFrankensteinState      = CMoveBaseState
CMoveStateRef           CMoveDescriptor ; [G] ptr<CMoveBaseState>
CLayeredStateRef        = CMoveStateRef
CMoveGroup              CMoveDescriptorGroup ; ver v ; [v>0] u8 branchEnable
CDoNothing              = CMoveDescriptorGroup
CMoveComment            CMoveDescriptor ; u8 popup
CMoveDefinition         ver v ; s32 variation ; v==0 ? CMoveDescriptorGroup : CBaseAnimGroup
CSyncDefinition         = CMoveDefinition

CAxialBlendAnimGroup    [G] u8 axisValueID (else str) ; CBaseAnimGroup ; ver v ; [v>3] u8 scaleDuration
CLayeredParameter       ver v ; [v>1] s32 spliceBlendMode ; data boneWeights
                        CMoveDefParameter ; [v>3] f32 worldOffset ; [v>4] f32 blendOutTime
CLayeredAxialBlend      ver v ; [v>=2] u8 spliceBlendMode ; data boneWeights
                        CAxialBlendAnimGroup ; [v>3] f32 worldOffset ; [v>4] f32 blendOutTime
CTimeControlledLayeredParameter  ver v ; CLayeredParameter ; [G] u8 timeSourceID (else str) ; [v>1] f32,f32
CTimeControlledMoveParameter     ver v ; CMoveDefParameter  ; [G] u8 timeSourceID (else str) ; [v>1] f32,f32
CFrankensteinParameter  ver v ; CMoveDescriptorGroup ; [v>=2] u32 poseNameHash ; [v>=3] f32 stopTime
                        [v>=4] s32 speedMode, f32 customSpeed
CSyncDefParameter       ver v ; [v>=8] u8 ; [v>=7] u8 ; [v>=6] u8 ; [v>=1] u8 ; f32 syncTime
                        [v<=1] f32,f32,u32,f32,f32,f32
                        (v<5 || G) ? u8 entityID : str ; v<2 ? CMoveDescriptor : CMoveDefParameter

CMoveObjectRef          ptr<CMoveObject>          (named files: 16-byte GUID + str instead)
CTransitionLink         ver v ; CMoveObject ; [v>0] f32 blendTime, u32 blendType, f32 blendRate, CMoveObjectRef
                        [v==2] ptr<CBaseAnimGroup> ; [v>2] ptr<CMoveDescriptorGroup>
CNotInterruptibleLink   ver v ; CMoveObject ; [v>0] CMoveObjectRef

CAnimTechIKPath / CAnimTechAttach / CAnimTechPossession   = CAnimTech
CAnimTechAnchor         CAnimTech ; ver v ; sid anchorPartName
                        [v==1] u8 ; [v>=3] u8 followTerrain ; [v>=4] u8 disablePhysics ; [v>=6] u8 disable
CAnimTechRagdoll        CAnimTech ; f32 physicsEnable ; f32 muscleIntensity

TMoveCriteriaEqual<bool|uint8> / NotEqual   u8 value ; CMoveCriteria
TMoveCriteriaEqual<int>  / NotEqual<int>    s32 value ; CMoveCriteria
TMoveCriteriaPerc<int|float>                u8 percentage ; CMoveCriteria
TMoveCriteriaIntv<int>                      ver v ; s32 lo, s32 hi ; [v>1] u8 inclusive ; CMoveCriteria
TMoveCriteriaIntv<float>                    ver v ; f32 lo, f32 hi ; [v>1] u8 inclusive ; CMoveCriteria
TMoveCriteriaIntv<CAngle>                   f32 lo, f32 hi ; CMoveCriteria
CMoveCriteriaEnumEqual / EnumNotEqual       ver v ; [G or v==0] s32 value ; CMoveCriteria
CMoveCriteriaEntityIDEqual / NotEqual       [G] u8 value (else str) ; CMoveCriteria
```

Class populations in the two files, which is the graph's real shape:

| Class | `movemgr.bin` | `dlc1.bin` | | Class | `movemgr.bin` | `dlc1.bin` |
|---|---:|---:|---|---|---:|---:|
| `CMoveDefParameter` | 4,042 | 947 | | `CAxialBlendAnimGroup` | 204 | 59 |
| `CMoveGroup` | 3,806 | 1,211 | | `CMoveDefinition` | 188 | 0 |
| `CMoveCriteriaEnumEqual` | 2,539 | 763 | | `TMoveCriteriaEqual<int>` | 177 | 98 |
| `CLayeredParameter` | 1,848 | 471 | | `CFrankensteinParameter` | 176 | 0 |
| `CAnimTechAnchor` | 1,845 | 188 | | `TMoveCriteriaIntv<CAngle>` | 168 | 0 |
| `CAnimTechIKPath` | 1,778 | 933 | | `CSyncDefinition` | 147 | 0 |
| `CMoveState` | 1,345 | 21 | | `CSyncState` | 127 | 0 |
| `CTransitionLink` | 1,146 | 252 | | `CNotInterruptibleLink` | 106 | 3 |
| `TMoveCriteriaIntv<float>` | 739 | 215 | | `CTimeControlledMoveParameter` | 87 | 5 |
| `CLayeredAxialBlend` | 440 | 287 | | `CMoveStateRef` | 75 | 0 |
| `CSyncDefParameter` | 294 | 0 | | `CTimeControlledLayeredParameter` | 70 | 60 |
| `TMoveCriteriaEqual<bool>` | 229 | 149 | | `CMoveComment` | 53 | 14 |
| `CLayeredState` | 223 | 18 | | `CMoveCriteriaEnumNotEqual` | 29 | 3 |
| `CDoNothing` | 213 | 25 | | `CLayeredStateRef` | 13 | 0 |
| | | | | `CFrankensteinState` | 5 | 0 |

Eleven of the 47 declared classes never appear in either shipped file: `CMoveBaseState` and
`CMoveDescriptorGroup` are abstract bases, and `CAnimTechAttach`, `CAnimTechPossession`,
`CAnimTechRagdoll`, `CMoveValueDef`, `CPlayerMoveState`, `TMoveCriteriaNotEqual<*>`,
`TMoveCriteriaPerc<*>` and `TMoveCriteriaEqual<uint8>` are supported by the loader but unused by
retail data.

### What the named twins add

The named files are the same graph with authoring metadata woven in, and the additions are now
mostly known — enough to walk `movemgrnamed.bin` through hundreds of objects, though not yet to the
end:

- `CMoveObject` gains `str name` + a **16-byte GUID** after its version. This is where every state,
  group and parameter name lives, and it is what makes the named files roughly twice the size.
- `sid` (`CStringID`/`CPathID`) gains its source string, so `TransferStringID` becomes
  `u32 hash` + `str`.
- `CMoveObjectRef` stops being a pointer and becomes a **16-byte GUID + the target's name** —
  authoring needs stable identity across edits, where the loadable form uses stream indices.
- `CMoveBaseState`'s `stateNameHash` and `aliasID` each become hash + string.
- The blend-set block gains names: a set name, a category name each, and for each category two runs
  of `poseCount` strings — the pose names and their mirror partners
  (`RightPass`↔`LeftPass`, `RightFront`↔`LeftFront`).

At least one authoring-only field remains unaccounted for around `CTransitionLink`, which is where a
full named walk still stops. Since no shipped binary can read these files, the disassembly cannot
settle the remainder — only more differential reading against the loadable twin can.

## Writing MOVE files

**JackAll reads and writes MOVE graphs.** `jackall-cli move decode / encode / verify` converts to
and from [the XML form](#an-editable-xml-form) and checks a graph reads back to itself, and the
app's **Move tab** browses a graph as the ownership tree it reads back as, labelling criteria with
the channel and enum value they test.

```
jackall-cli move decode movemgr.bin --names movemgrnamed.bin
jackall-cli move encode movemgr.xml
jackall-cli move verify dlc1.bin
```

A Python reference codec lives beside it in `tools/misc/move-python-reference/` — `move_codec.py`
reads and writes, `move_xml.py` converts to and from XML, and `move_expand.py` clones a weapon's
states onto a new index. One
set of layout functions drives both directions: the reader records each primitive, the writer
replays the recorded values while emitting bytes, so version gates and list terminators take the
same branches without being special-cased.

```
python move_codec.py movemgr.bin dlc1.bin
python move_expand.py dlc1.bin out.bin --from 42 --to 44 --prefix MyWeapon
```

Measured results:

| File | Bytes | Objects | Round trip |
|---|---:|---:|---|
| `movemgr.bin` | 1,858,293 | 22,117 | byte-identical |
| `dlc1.bin` | 473,404 | 5,724 | byte-identical |
| a generated expansion | 916,750 | 11,029 | byte-identical |

### An editable XML form

`move_xml.py` projects the whole graph to XML and builds it back. It is an interchange format, not
one the game loads — the same relationship `.fcb` has with Gibbed's XML — and it is **not**
`movemgrnamed.bin` either: the engine's own authoring form addresses objects by GUID rather than by
stream position, and since no shipped executable can read it, it cannot be the basis for something
that produces a loadable file. What the XML borrows from the engine is the *vocabulary*. Every
`Transfer` call passes its field name as a debug string, so the element names below are the
engine's own, not invented:

```xml
<obj n="CMoveCriteria" class="CMoveCriteriaEnumEqual" id="19">
  <ver n="CMoveCriteriaEnum" v="1"/>
  <s32 n="m_Value" v="42" enum="SawedOffShotgun"/>
  <ver n="CMoveCriteria" v="4"/>
  <u8 n="m_eValueID" v="17" channel="EquippedWeapon"/>
  <ver n="CMoveObject" v="1"/>
  <s32 n="m_logicOperator" v="0"/>
</obj>
```

Pass `--names movemgrnamed.bin` and criteria are labelled with the channel and enum value they
test, recovered from the named twin's channel table. Those `channel=` and `enum=` attributes are
informational — the builder ignores them, so an annotated document still rebuilds byte-for-byte.

```
move_xml.py export dlc1.bin dlc1.xml --names movemgrnamed.bin
move_xml.py build  dlc1.xml out.bin
move_xml.py verify dlc1.bin --names movemgrnamed.bin
```

Verified `bin → xml → bin` byte-identical on `movemgr.bin` (25.2M of XML), `dlc1.bin` (6.7M) and a
generated expansion (13.1M). Two details make that exactness possible: floats are written as decimal
only when re-parsing reproduces the same four bytes and fall back to `hex=` otherwise, and string
fields that are not clean ASCII — such as the `Extension` garbage — are written as `hex=` too.

### The pointer graph is the hard part

Objects are addressed by **their position in registration order**, not by any stored id.
`CLoadStoreMap::AddToMap` appends one entry per object and hands back `count - 1`;
`CSerialStream::IndexInMap` returns that index, or `-1` for an object not yet written. Registration
happens **pre-order** — after the `ClassType` word, before the object's own `Serialize` — so a
parent always has a lower index than anything it contains, and a back-reference can only ever point
backwards. There are no forward references in the format.

A writer therefore has one real obligation: **emit objects in the same order the reader will
re-create them, and number them as it goes.** The reference codec discards the indices it read and
recomputes every back-reference from object identity on write; that both files still come out
byte-identical is the proof that this model is right.

### Naming a state

`CPathID` is **CRC32 of the lowercased name** — `crc32("dlc1_aim") = 0x32F3C893`, which is exactly
the `m_stateNameHash` the shipped `dlc1.bin` stores for that state. The same hash is used for
resource paths (see [`depload.dat`](./depload.md)).

Two `u32`s at the end of every `CMoveBaseState` carry the state's identity, and the second one is
easy to misread:

| Field | Meaning |
|---|---|
| `m_stateNameHash` | `CPathID` of this state's own name — must be unique |
| `aliasID` | **`CPathID` of the parent state to graft onto**, or `0xFFFFFFFF` for none |

`aliasID` is not an alias. `CMoveMgr::MergeMoveExpansion` reads it from `state->field_0x20`, looks it
up with `GetStateIDFromStringID` against the *already-loaded base graph*, and calls
`InsertExpansionMove` on whatever it finds. All 39 states in `dlc1.bin` set it, and **all 39 resolve
to states that exist among the base graph's 1,700** — for instance `DLC1_Aim`
(`0x32F3C893`) grafts onto `Pawn_Generic_Aim` (`0x681235D2`). A state whose `aliasID` does not
resolve is added to the machine but attached to nothing.

### Adding a weapon's states

The cheapest way to author a new weapon's animation coverage is not to synthesise states but to
**clone an existing weapon's and retarget them**, which is what `move_expand.py` does:

1. Find every state whose subtree holds a `CMoveCriteriaEnumEqual` on channel 17 with the donor's
   index.
2. Deep-copy each subtree, remapping back-references that point *inside* it to the copies and
   leaving those that point outside alone.
3. Give each copy a fresh `m_stateNameHash`; leave `aliasID` alone so the copy grafts onto the same
   base state the original does.
4. Rewrite the weapon comparands, bump `nbState`, and append the copies to the state machine's list.

Cloning DLC1's `SawedOffShotgun` (index 42) onto index 44 produces 16 states / 5,305 new objects and
41 retargeted criteria, and the result re-parses cleanly. What it does **not** do is supply
animation: the cloned states still name the donor's `.mab` clips through their
`CMoveDefParameter.m_animNameHash`, so a real weapon needs its own clips and those hashes repointed.

## `EquippedWeapon` — the weapon ceiling

Channel 17 (`EquippedWeapon`) and channel 18 (`DesiredWeapon`) each carry the same **44-entry** value
list, in this exact order:

```
 0 None                11 IED                 22 UZI                 33 Phone
 1 DesertEagle         12 Mortar              23 Dragunov            34 Browning
 2 AK47                13 RPG7                24 Watch               35 MK19
 3 Machete             14 MAC10               25 FNFal               36 Compass
 4 Molotov             15 M249SAW             26 G3KA4               37 Ratchet
 5 Ithaca              16 SPAS12              27 USAS12              38 SilencedMakarov6P9
 6 M67                 17 CarlGustaf          28 StarModelP          39 Dart_Rifle
 7 LPO50               18 PKM                 29 M79                 40 MGL140
 8 Sniper              19 M16                 30 AS50                41 Crossbow
 9 MapCompass          20 Makarov             31 Binoculars          42 SawedOffShotgun
10 M249                21 MP5                 32 FlareGun            43 SilencedShotgun
```

This index is exactly what a weapon archetype's `CFCXWeapon.iAnimationValue` holds — confirmed
against the shipped DLC data, where `DLC1.Crossbow` is `41`, `DLC1.SawedOffShotgun` is `42` and
`DLC1.SilencedShotgun` is `43`.

:::note[Corrects an earlier community claim]
`iAnimationValue` has been described community-side as "reportedly affects which crosshair is used"
(repeated in [data-recipes](../modding/data-recipes.md)). That is wrong. It is the `EquippedWeapon`
index; the crosshair comes from `CommonProperties.crosshairMagmaAreaName`, a plain string naming a
Magma area.
:::

:::danger[This page previously claimed a hard 44-weapon ceiling. That was wrong.]
An earlier revision argued that the 44-entry list is declared in `CMoveValueContainer`, that an
expansion cannot redeclare it, and therefore that `iAnimationValue` "has to be one of 0–43". Every
step of that reasoning fails against the bytes and the disassembly:

- **The list is not in the loadable file.** `movemgr.bin` contains no channel names and no enum
  value names — zero occurrences of `EquippedWeapon` or `SawedOffShotgun`. Each channel is five
  bytes of `{type, mirrorable}`. The 44 names live *only* in `movemgrnamed.bin`, which the engine
  refuses to load.
- **The runtime has no enum table to overflow.** `GetValueNameFromID` returns
  `"INVALID VALUENAME"` for every index, and an Enum channel is stored as a plain 4-byte slot.
- **Criteria compare integers.** `m_eValueID` is one byte of channel index, `m_Value` a signed
  32-bit comparand. Nothing range-checks either against a declared value count.
- **`iAnimationValue` is a plain `int`.** It is registered on `CEquipmentBase` at offset `0x20`
  (`RegisterProperties`, `0x09020150`) with no clamp — not on `CFCXWeapon`, as stated earlier.
- **The string evidence was misread.** Across all six `Dunia.dll` builds in `tmp/compare-dlls/`,
  exact-case `SawedOffShotgun` appears **0** times and `EquippedWeapon` **0** times. What is present
  — once per build, v1.0 included — is lowercase `sawedoffshotgun`, which is the *model/animation
  package* name, not the MOVE enum value. It says nothing about enum reservation.

The number 44 is authoring metadata. The engine only ever sees the integer.
:::

What *is* measured, and does constrain you:

1. `LoadMoves` rejects any base file that does not declare exactly **105** channels. That count is
   fixed; the *values* a channel can hold are not.
2. An expansion package contains no value container at all — `dlc1named.bin` has zero `CVvM` and
   zero `dVvM` records. It does not need one: its criteria are `{u8 channel, s32 value}` pairs.
3. Consequently an expansion **can** reference `EquippedWeapon == 44` exactly as cheaply as `== 43`.
   There is no declaration to extend.

The real requirement for a 45th weapon is therefore not permission but *coverage*: index 44 has no
states behind it, so a character holding it would find no matching move and fall through to whatever
the graph's defaults are. Supplying those states is the actual work, and it is the ordinary
expansion path — the same one DLC1 used.

Two caveats worth stating plainly, because neither has been tested in game:

- Whether the code that drives channel 17 each frame passes `iAnimationValue` through unclamped has
  not been traced end to end; only the property registration and the container's storage were.
- What the graph does when *no* state matches is unknown, and could be anything from a T-pose to a
  benign fallback.

Note that the six reserved `dlc1`…`dlc6` names that exist for HUD icons and kill codes (see
[Adding a weapon](../modding/adding-a-weapon.md)) have **no counterpart here** — MOVE reserves
nothing, because there is nothing to reserve. One asymmetry is real, though: the base
`movemgr.bin` package list already ships `dlc1_crossbow`, but **not** `dlc1_sawedoff_shotgun` or
`dlc1_silenced_shotgun`.

## What an expansion actually contains

`dlc1named.bin`'s states are paths, and the clip bindings are legible as text:

```
DLC1_Aim/First/Stand/IronSightTransition/SawedOff_Shotgun/dlc1_1stge_uppb_aim2iron_+000fw_sesos_i1
  graphics\characters\_common\animations\weapons\dlc\sawedoff_shotgun\dlc1_1stge_uppb_jumpstart_+000fw_sesos_i1
  EquippedWeapon
  IKPath_L Hand_  ->  dlc1_sawedoff_shotgun
```

The IK target and the weapon key are the **model/skeleton name** (`dlc1_sawedoff_shotgun`), which is
the same string a weapon archetype puts in `CSimpleAnimationComponent.sPartName` and the same string
`depload` uses to name the weapon's `CAnimationPackageResource`. That name — not the archetype path
— is what ties a weapon to its animation set.

## Open questions

**The loadable format is fully decoded, and both read and write are implemented.** See
[Writing MOVE files](#writing-move-files). Nothing about reading or emitting a `0x50000` MOVE file
is open.

Still open:

- **The named twins are ~90% decoded, and the last of it may not be recoverable from the
  binaries.** See [What the named twins add](#what-the-named-twins-add). No shipped executable can
  read a `0x20000` file, so the remaining authoring-only fields have to come from differential
  reading against the loadable twin rather than from disassembly.
- **Nothing built with the writer has been loaded by the game yet.** Every claim below is verified
  against the format and against the shipped data; none of it is verified against a running
  `Dunia.dll`. Producing a file the parser accepts is necessary, not sufficient.
- **Whether the `files/Move File` config key can be overridden** from `OverrideEngineConfig.xml` is
  untested, and remains the cheapest experiment available. Note the earlier framing of this
  experiment — "a replacement graph with a longer `EquippedWeapon` list" — was based on a
  misunderstanding: there is no list in the file to lengthen.
- **Whether the engine drives channel 17 unclamped** with an `iAnimationValue` of 44 or more, and
  what the graph does when no state matches, are the two in-game questions the weapon work actually
  turns on.

## Related

- [`.mab` — animation clips](./mab.md) — what MOVE selects between
- [`.skeleton`](./skeleton.md) — the rig the clips drive
- [`depload.dat`](./depload.md) — the `CAnimationPackageResource` that ties a weapon's clips together
- [Adding a weapon](../modding/adding-a-weapon.md) — how `iAnimationValue` picks a weapon's clips
