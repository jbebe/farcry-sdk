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

### The object stream

Everything after the header is a graph of polymorphic objects written through the engine's generic
`ISerialStream`. `TransferPointer<T>(fieldName, typeName, &ptr)` opens an object and gets back a
disposition code:

| Code | Meaning |
|---|---|
| `0x333` | new object — read a `u32 ClassType`, allocate via `CGlobalSerialInfo::Allocate(ClassType, 'MvOb')`, then call the object's own `Serialize` |
| `0x111` | pointer to an object already read — patch the pointer, read nothing |
| `0x555` | type conversion of an already-read object |
| `0x222` | re-read `ClassType` and serialize into the existing object |

`'MvOb'` (`0x4D764F62`) is the factory category constant for the whole MOVE subsystem.

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

### The value-channel table

The single `CMoveValueContainer` is the graph's blackboard: a fixed list of named channels the game
writes each frame and that transition criteria read. Its record opens with the class id, the channel
count, and the container's own name (`DefaultValueContainer`); the channel table itself begins at
`0x7D` in `movemgrnamed.bin`.

Each channel is:

```
offset  size      field
0       4         type (EMoveValType)
4       1         m_fMirrorable
5       4         nameLen
9       nameLen   name
        —— only when type == 5 (Enum) ——
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

Walking that record shape from `0x7D` consumes exactly 105 channels and lands on `0x14B4` with no
drift, which is what confirms the layout. Channels are indexed in file order, and the index is what
the rest of the engine stores — `CMoveValueContainer::GetValueIDFromName` maps a `CStringID` to the
index through an `ldiv`-based scramble on `0x1F31D`, the same non-CRC32 hash the
[Magma menu system](../engine-internals/magma-menu-system.md) uses for its page table.

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

`Mirror` is the only channel flagged mirrorable in the whole table, which is a useful semantic check
that the flag byte was read in the right position.

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

Three measured facts combine into a hard limit:

1. The channel list, including these 44 values, lives only in `CMoveValueContainer`, and an
   expansion package contains no value container — `dlc1named.bin` has **zero** `CVvM` and **zero**
   `dVvM` records. Every `EquippedWeapon` occurrence in it is a criteria reference, not a
   declaration.
2. `LoadMoves` rejects any base file that does not declare exactly 105 channels.
3. All six `Dunia.dll` builds in `tmp/compare-dlls/` — including v1.0, which predates DLC1 — already
   contain the `SawedOffShotgun` string. The three DLC weapon slots were reserved before the DLC
   shipped.

**So a new weapon cannot introduce a 45th `EquippedWeapon` value by shipping a DLC-style expansion.**
Its `iAnimationValue` has to be one of 0–43. The routes out are to rewrite `movemgr.bin` itself
(which needs a writer for this format — see [Open questions](#open-questions)), to repoint the
`files/Move File` config key at a replacement, or to reuse an existing index. Note that the six
reserved `dlc1`…`dlc6` names that exist for HUD icons and kill codes (see
[Adding a weapon](../modding/adding-a-weapon.md)) have **no counterpart here** — the MOVE enum
reserves nothing.

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

- **No writer, and no full record decode.** The header, class-ID table, channel table and merge
  semantics are solid. The per-state record interior — transition links, criteria trees, axial blend
  groups, `CMoveDefParameter` payloads — was not walked. Producing a new expansion package means
  decoding those, or diffing an edited `dlc1.bin` against the original.
- **Where the channel and enum *names* are written is not fully traced.** `CMoveValueContainer::Serialize`
  as decompiled transfers only `{m_eMVType, m_fMirrorable}` per channel, yet the names and enum value
  lists are demonstrably inline at the offsets given above. Some part of the named stream's
  `BeginObject`/`TransferNamedEnum` path emits them; that path was not opened.
- **Whether `dwFileFormat` can be edited to make a named file loadable** is untested. Clearing
  `0x20000` would pass `CreateFromStream`'s gate, but the stream would then be read without the name
  records the flag implies and would almost certainly desynchronize.
- **Whether the `files/Move File` config key can be overridden** from `OverrideEngineConfig.xml`, and
  whether a replacement graph with exactly 105 channels but a longer `EquippedWeapon` list is
  accepted, is untested — and is the cheapest experiment available.

## Related

- [`.mab` — animation clips](./mab.md) — what MOVE selects between
- [`.skeleton`](./skeleton.md) — the rig the clips drive
- [`depload.dat`](./depload.md) — the `CAnimationPackageResource` that ties a weapon's clips together
- [Adding a weapon](../modding/adding-a-weapon.md) — where the `iAnimationValue` ceiling bites
