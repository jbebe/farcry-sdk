---
sidebar_position: 9
---

# `.sav` — Savegame Container Format

:::info[Verified via reverse engineering]
Confirmed byte-for-byte against a real save (`178430170947.sav`, 1,854,505 bytes, from
`Documents\My Games\Far Cry 2\Saved Games\`): offsets were hypothesized from the raw hex, then
checked against the decompiled writer/reader functions' own arithmetic. Traced primarily against
**`FarCry2_server`** (the Linux dedicated-server binary), not `Dunia.dll` — see "Which binary" below.
No prior community tooling documented this format at the byte level.
:::

Far Cry 2 saves to `Documents\My Games\Far Cry 2\Saved Games\<digits>.sav` (see
[save-data path](../engine-internals/save-data-path.md) for how that directory is resolved). The
file is a flat, uncompressed, unencrypted concatenation of four sections, each serialized by its own
C++ class:

```
[0x0000]  CGameFileHeader base fields          20 bytes   fixed size
[0x0014]  + CCampaignGameFileHeader extension   variable   2 length-prefixed strings + 12 bytes
[0x0039]  CScreenShot (thumbnail)               variable   16-byte header + pixel blob + metadata
[0xB44D]  CCampaignGameFileData                 variable   DLC-id string list...
[0xB45D]    → an embedded, ordinary .fcb blob   variable   PersistenceDB entity/state dump
```

Every section boundary lines up exactly with the writers' own `GetSaveSize()` arithmetic — there is
no padding or alignment between sections. Measured against the sample file:

| Section | Size formula | This save's size | File offset |
|---|---|---|---|
| `CGameFileHeader` base | constant `0x14` | 20 | `0x00`–`0x13` |
| `CCampaignGameFileHeader` extra | `0xC` fixed + 2 length-prefixed strings | 37 | `0x14`–`0x38` |
| `CScreenShot` | `16 + W·H·channels·bitsPerChannel/8 + 4 + Σmetadata` | 46,100 | `0x39`–`0xB44C` |
| `CCampaignGameFileData` DLC list + reserved field | `4 + Σ(4+strlen)` per DLC `+ 4` | 16 | `0xB44D`–`0xB45C` |
| embedded `.fcb` blob | `Fcb`-header-compatible; rest of the file | 3,753,895+ | `0xB45D`–EOF |

## Which binary this was traced against

`Dunia.dll` (the Windows client — see [engine overview](../engine-internals/overview.md)) contains
the same `.sav`/`CGameFile*`/`CScreenShot`/`CPersistenceDB` code, but none of it is exported or named,
and its RTTI hasn't been recovered for these classes. The Ghidra project also contains a third
program besides `Dunia.dll` and the launcher: **`FarCry2_server`**, the Linux dedicated-server ELF
(`list_segments` confirms `.dynamic`/`.got.plt`, ~`0x08048000` load base; `list_imports` shows POSIX
imports like `pthread_create`/`gethostbyname`/`listen`). Its symbols are GCC/Itanium-mangled and the
binary is largely unstripped (`.symtab`/`.strtab` present), so real C++ class and method names survive
even though a headless server never actually writes a player save. Because the dedicated server links
the same shared engine source, it's the better source for names here; byte offsets below were
independently re-verified against the real Windows-written save file regardless. Addresses in the
`0x08xxxxxx`–`0x0axxxxxx` range in this note belong to `FarCry2_server`, not `Dunia.dll`.

## Section 1 — `CGameFileHeader` base (20 bytes, offset `0x00`)

`CGameFileHeader::GetSaveSize()` (`0x091e3810`) hardcodes a return of `0x14` — this base header is
always exactly 20 bytes. Its `WriteToFile`/`ReadFromFile` weren't located under that name, so the
field meanings below are inferred from the real bytes rather than decompiled directly:

| Offset | Size | Field | Measured value | Confidence |
|---|---|---|---|---|
| `0x00` | 4 | u32, ID/tag | `0x0000000A` (10) | low — not a plausible timestamp; likely a small save-type/slot enum or a leftover `InvalidID` sentinel |
| `0x04` | 4 | float, likely player X | ≈2621.3 | medium |
| `0x08` | 4 | float, likely player Y | ≈2109.1 | medium |
| `0x0C` | 4 | float, likely player Z | ≈17.9 | medium — plausible elevation on world1 |

The 3-float reading is circumstantial: [command-line args](../engine-internals/command-line-args.md)
already predicted a `PlayerPos`-shaped property gets read back after `-load`, and the X/Y magnitudes
match `world1`'s map extents. Treat as a strong hypothesis, not a confirmed field mapping.

## Section 2 — `CCampaignGameFileHeader` extension (offset `0x14`)

`CCampaignGameFileHeader::GetSaveSize()` (`0x0896ef10`):

```c
GetSaveSize() = CGameFileHeader::GetSaveSize() + 0xc
              + GameFileUtils::GetStringSaveSize(worldName)
              + GameFileUtils::GetStringSaveSize(playerName);
```

`GetStringSaveSize` costs `4 + strlen` — a u32 length prefix with **no null terminator** (unlike the
strings inside the embedded `.fcb` blob, which are null-terminated). Measured:

| Offset | Size | Field | Value |
|---|---|---|---|
| `0x14` | 4+6 | length-prefixed string | `"world1"` |
| `0x1E` | 4+11 | length-prefixed string | `"Paul_Ferenc"` (player/character name) |
| `0x2D` | 4 | u32 | 1 |
| `0x31` | 4 | u32 | 8 |
| `0x35` | 4 | u32 | 2 |

No `GetDifficulty`/`GetAct`/`GetChapter` accessor was found on this class to pin down the trailing
three u32s definitively (a `Difficulty`-named accessor cluster exists elsewhere in the engine, but
wasn't confirmed wired to this class). Plausible reading of `(1, 8, 2)`: difficulty tier, an
act/chapter progress marker, and a third small enum — not confirmed.

## Section 3 — `CScreenShot` (thumbnail, offset `0x39`)

`CScreenShot::WriteToFile` (`0x091eaa00`) and `CScreenShot::ReadFromFile` (`0x091ebae0`) are exact
mirrors of each other and both match the real file byte-for-byte:

| Offset | Size | Field | Value |
|---|---|---|---|
| `0x39` | 4 | width (u32) | 128 |
| `0x3D` | 4 | height (u32) | 90 |
| `0x41` | 4 | channels (u32) | 4 (RGBA/BGRA) |
| `0x45` | 4 | bits per channel (u32) | 8 |
| `0x49` | W·H·ch·bpc/8 | raw pixel bytes | 46,080 bytes |
| `0xB449` | 4 | metadata-entry count (u32) | 0 |

Size formula confirmed exactly: `width * height * channels * bitsPerChannel / 8`. Pixel channel order
(RGBA vs BGRA) wasn't distinguished from this one sample — low, similar-magnitude values across all 3
channels are consistent with either, matching a dark/foliage-heavy screenshot. The per-entry metadata
format (`WriteMetaDataInfoToFile`/`ReadMetaDataInfoFromFile`) wasn't traced — this sample has zero
entries. Capture-side entry points (`CGameFilesService::GrabScreenshotEv`, `CScreenShot::Capture`)
weren't decompiled.

## Section 4 — `CCampaignGameFileData` (offset `0xB44D`): DLC list + embedded `.fcb` blob

`CCampaignGameFileData::GetSaveSize()` (`0x0896ee90`):

```c
GetSaveSize() = 4                                        // DLC-string count
              + Σ GameFileUtils::GetStringSaveSize(dlc)   // one entry per active DLC id
              + 4                                         // extra fixed field, purpose unconfirmed
              + persistenceObj->GetSaveSize();            // virtual call — the embedded FCB blob
```

| Offset | Size | Field | Value |
|---|---|---|---|
| `0xB44D` | 4 | DLC count (u32) | 1 |
| `0xB451` | 4+4 | length-prefixed string | `"dlc1"` |
| `0xB459` | 4 | u32 | 0 (purpose unconfirmed) |
| `0xB45D` | — | embedded `.fcb` blob starts here, runs to EOF | — |

### The embedded blob is an ordinary `.fcb` file

Bytes at `0xB45D` decode exactly per the [`.fcb` header](./fcb.md), with no wrapper, no extra length
prefix, and no compression:

```
0xB45D   4    magic (u32)             0x4643626E "FCbn"   — matches Fcb_MagicConstant()
0xB461   2    version (u16)           2                    — matches Fcb_SupportedVersionConstant()
0xB463   2    flags (u16)             0
0xB465   4    totalObjectCount (u32)  73,200 (0x11DF0)
0xB469   4    totalValueCount (u32)   73,199 (0x11DEF)
0xB46D   —    root object tree (Fcb_ParseObject rules apply verbatim)
```

Walking the root object by hand against `.fcb`'s documented parse rules decodes cleanly — its first
value is a 14-byte string, `"Addi Mbantuwe"`, a Far Cry 2 buddy/companion NPC name. This is a
`CPersistenceDBRec`/`CBindingHierarchyDBRec` entry: **the savegame's bulk content is the entire
`PersistenceDB`** — every spawned/moved/killed entity's state, from buddies to individual dropped
items — serialized through the exact same generic `.fcb` writer used for the game's shipped data
files, not a bespoke savegame format. This is why values like jump height or outpost-clear timers are
"cached in savegames": they're ordinary properties on ordinary entities in this same tree, no
different in kind from anything else persisted here.

**Practical consequence**: JackAll's existing `FcbDocument` reader/writer, already validated against
shipped `.fcb` fixtures, reads this blob directly once sliced out at `0xB45D` — no separate binary
format work is needed to read (or, cautiously, write back) a save's entity tree, only the four wrapper
sections above.

## The `PersistenceDB` tag and field vocabulary

`CPersistenceDB::SaveDB` (`0x0967e350`) walks the DB's binding-hierarchy tree and, per entity,
serializes through `CNomadObjectDescriptor::SaveState` using a set of named tags recovered directly
from the decompiled body: `Tag_HierarchiesQueue`, `Tag_EntityId`, `Tag_Hierarchy`, `Tag_Entities`,
`Tag_HierarchyRecord`, `Tag_Id`, `Tag_Record`, `Tag_State`, `Tag_Description`, `Tag_HierarchyId`,
`Tag_OmniEntities`. These are the human-readable field names whose `GetNameHash`/`CRC32_Hash` values
become the `nameHash`s stored in each `.fcb` value entry — the same mechanism the [`.fcb`
page](./fcb.md) documents for ordinary data files.

Two vocabularies coexist in a save's exported tree, and it matters which one a given hash belongs to:

- **The wrapper/bookkeeping layer** (`CPersistenceDBRec`/`CBindingHierarchyDBRec` and the `Tag_*`
  structural tags above) — names that exist only in the game's compiled code, never in a shipped data
  file, so the community-built `binary_classes.xml` catalog (built from strings visible in shipped
  `.fcb` files) never had a chance to capture them.
- **Entity component data embedded verbatim** — a persisted entity's own live component tree
  (`CIgnitorComponent`, `CCompoundPhysComponent`, `CPersistComponent`, `RootNode`, ...) reuses
  `entitylibrary.fcb`'s own class vocabulary exactly, because it's the same live C++ objects being
  serialized. `binary_classes.xml` resolves this part for free: in one real save, 37% of `<object>`
  tags and 14% of `<value>` tags already resolve via the existing per-class lookup, with no extra work.

Both `CBindingHierarchyDBRec::RegisterProperties` and `CPersistenceDBRec::RegisterProperties`
(`0x09679a93`/`0x09679b22`) call `CNomadObjectDescriptor::PushBackMember` once per registered field
with the field's real name as a literal string — the same technique (find a class's `ms_descriptor`,
find its xrefs, decompile the registrar, read the literal names) generalizes to any other persisted
record type. `CBindingHierarchyDBRec` registers `MemoryUsage`, `PersistType`, `BindingHierarchy`;
`CPersistenceDBRec` registers `MemoryUsage` independently (a same-named but unrelated field on a
different class — their instance counts sum to the observed total, they don't collide).

Confirmed by CRC32-hashing each candidate tag/member name and matching it against real hashes in a
save's exported tree:

| Tag/field | CRC32 | Occurrences (one save) |
|---|---|---|
| `Id` | `0x2ABD43F2` | 4,870 |
| `State` | `0x6252FDFF` | 3,545 |
| `MemoryUsage` | `0x65A0E5B6` | 4,488 (sum of both registering classes) |
| `EntityId` | `0x0F5E4BAA` | 2,773 |
| `HierarchyId` | `0xA9100FC2` | 2,334 |
| `PersistType` | `0x4A1FC981` | 2,154 |
| `Record` | `0x9C989AA7` | 2,651 |
| `HierarchyRecord` | `0x7A2B069C` | 2,154 |
| `Description` | `0xEB78CFF1` | 485 — conditional (`GetChildDescription` branch), not written for every record |
| `Entities` / `Hierarchy` / `HierarchiesQueue` / `OmniEntities` | various | 2 each — top-level container tags, used once or twice near the root |
| `BindingHierarchy` | `0xE2C5EA2C` | 0 — registered, but never seen as a plain value; likely a child-object reference rather than a scalar |

### Closing the remaining gap: dictionary attack against the binary's own strings

Beyond the class-scoped and hand-curated matches above, `binary_classes.xml`'s **flat** namespace (any
member/class name anywhere in the file, regardless of which class declared it) resolves more by
coincidence of English: 51 of 820 distinct value hashes and 6 of 246 distinct object-type hashes in one
real save matched, each verified by both hash and matching byte-length (to reject false-positive
collisions like `Id`, whose 4-byte declared width doesn't match the save's consistently 8-byte field of
the same hash — `Flags` similarly rejected on a 1-vs-4-byte mismatch).

Since every field name reaching `PushBackMember` is a literal string sitting in the binary's own
rodata, a full scan of `FarCry2_server` and `Dunia.dll` for printable-ASCII runs (rather than reading
individual `RegisterProperties` functions one at a time) turned up the rest: CRC32-hash every
plausible-identifier string found in either binary and keep the ones matching a hash actually present
in a real save's tree. This resolved 964 additional hashes unambiguously (zero collisions among
matches). Combined:

| Source | Distinct hashes resolved |
|---|---|
| Hand-decompiled `RegisterProperties` tags | 9 |
| `binary_classes.xml` flat lookup, byte-length verified | 50 |
| Dictionary attack (binary string scan + CRC32 match) | 964 |
| **Union, one real save** | **1,023 / 1,046 = 97.8%** |

23 hashes remain unresolved in this sample (including `Id`, known by name but excluded above on a
byte-length mismatch). Notable recovered names: `KeyType`/`ValueType` (generic key/value pair
scaffolding, on nearly every node), `WorldMatrix` (64 bytes, one per `HierarchyRecord`),
`CurrentHealth`, `hidLastVelocity`, `CachedAnchorPosition`/`CachedAnchorOrientation`, a 61-hash weapon-
memento cluster (`MaxReliability`, `JammingBullet`, `RememberedAmmoInClip`, ...), a 54-hash AI
look/animation-state cluster (`UsingLook`, `AimAngles`, `BarkLookAngles`, `FovOverride*`, ...), and a
27-hash AI-brain/army-member block (`CurrentArmyMemberState`, `ThreatLevel`, `AlertLevel`,
`MercBrain`, `BuddyDownEnable`, ...). Root-level object names read as the save's overall shape:
`CampaignSave`, `PersistenceDB`, `BuddyManagement`, `MissionManagement`, `GameplayManagement`,
`WorldDiamonds`, `MainHud`, `BlueArmy`/`RedArmy`/`GreyArmy`/`NeutralArmy`.

JackAll ships this as `tools/JackAll/assets/savegame_field_names.tsv` (964 rows), applied by the Saves
tab after `binary_classes.xml`'s own resolution and a small hand-curated tag table, name-only —
deliberately kept out of the round-trip-critical `FcbClassDefinitions`/`FcbXml` machinery the Files
tab's mod-editing depends on, since neither resolution method is verified as rigorously as
`binary_classes.xml`'s own provenance.

## How a persisted entity's dynamic state is captured

**`CPersistenceDB::AddRecord(TEntityHandle<CEntity>, EPersistType, CBindingHierarchyDBRec*)`**
(`0x09679e90`) is called on every entity as it finalizes. After allocating a `CPersistenceDBRec` and
inserting it into the DB's hash table, it calls the entity's own polymorphic vtable slot `+0x10`
(an accessor for the entity's descriptor/node) and feeds the result into the generic
**`CNomadObjectDescriptor::SaveState`** — the same reflected-property serializer the [`.fcb`
writer](./fcb.md) uses. There is no savegame-specific "capture this entity's state" function: every
entity class captures whatever it wants persisted purely by having already called
`RegisterProperties`/`PushBackMember` for those fields, and `AddRecord` triggers that generic
machinery recursively for every child `CEntityProxy` too. This is called from
`CGhostManager::OnFinalize`, and mirrored on the read side by `RestoreEntity`'s `LoadState` call below.

There are 300+ anonymous `RegisterProperties` functions in `FarCry2_server`, one per entity/component
class, none individually attributable to a class name by a plain search. The dictionary-attack
technique above recovers plausible field *names* for large blocks of this state (the 27- and 54-hash
clusters read as one buddy/merc's full AI-brain block and a look/animation-state block respectively)
without needing to individually decompile and attribute each `RegisterProperties` call.

## Mod compatibility: a per-property overlay, not a full freeze

**`CPersistenceDB::RestoreEntity(TEntityHandle<CEntity>)`** (`0x0967c050`) runs on an
already-constructed, already-locked `CEntity` — it does two hashtable lookups by `EntityId` and, only
if a record is found, calls `CNomadObjectDescriptor::LoadState(entity, persistedNode)`, the same
generic reflected-property loader the [`.fcb` reader](./fcb.md) uses. If no record exists for that
`EntityId`, `RestoreEntity` does nothing. It's called as the last step of
`CGhostManager::OnFinalize(TEntityHandle<CEntity>)` — the tail of normal entity spawn/construction.

This settles the ordering: an entity is always spawned first from its current `entitylibrary.fcb`
definition, and only afterward does `RestoreEntity` conditionally overlay whatever specific properties
were captured for it — if any. It is a property-level overlay on a freshly-spawned object, not a
substitution of the entity's definition, and it's a no-op for entities with no persisted record.

Practical consequences for modding `entitylibrary.fcb` against an existing save:

- **Entities never persisted** (no record for that `EntityId`) spawn 100% from current data. A mod's
  changes apply immediately, no save-editing needed. Most of the map, on any given save, falls here —
  only entities that changed state during play get a record at all.
- **Entities that do have a persisted record** only have the specific captured properties overridden.
  Design-time tuning values that live only on the archetype and aren't part of an instance's dynamic
  state (weapon damage/range/reliability curves, AI perception thresholds) still take effect even for a
  persisted entity, since `LoadState` never touches fields it wasn't given data for. Genuinely dynamic
  per-instance state that *is* captured (position, health, inventory, hierarchy relationships) stays
  frozen at its persisted value regardless of later edits.
- The commonly reported "big chunks of `.fcb` are frozen in the save" behavior is real but
  entity-and-property-scoped, not global.
- Since `RestoreEntity` is a no-op with no record, **deleting an entity's persisted record from the
  save's embedded `.fcb` tree** forces it to respawn purely from current `entitylibrary.fcb` data next
  load — a coarser but far more tractable technique than reconciling individual values, and one that
  doesn't require knowing what any given `nameHash` means.

See [Entity-Library Overlap](#entity-library-overlap) below for which classes/fields this mechanism
actually touches in a real save, measured directly.

## Why the save filename is a bare number

**`GameFileUtils::GenerateCampaignGameFileName`** (`0x091ea6b0`) produces the `<digits>.sav` name. It
uses neither wall-clock time nor a save-slot index:

1. `CHighPerfTimer::GetTimeValue()` → `Gear::Time::GetCpuCycle()` → a bare `rdtsc()` — the CPU
   timestamp-counter register, a free-running cycle count with no calendar meaning.
2. `ndRandUInt()` — a small in-engine LCG PRNG (`globalRandom = globalRandom*0x343fd + 0x269ec3`,
   returning `(globalRandom >> 16) & 0x7fff`) is added as jitter.
3. The sum is formatted as a plain decimal integer and the `.sav` extension appended.

So `178430170947.sav`'s name is a cycle count plus a small random offset — a cheap, collision-resistant
unique ID, not a timestamp, slot number, or hash. It carries no ordering or date information.
`CFCXEditorGameFilesService::GenerateSaveFileName` (the custom-map/editor path) is a sibling using
`GameFileUtils::GenerateCustomMapFileName`, presumed to follow the same pattern but not decompiled.

## Unknowns

- `CGameFileHeader`'s own `WriteToFile`/`ReadFromFile` weren't located under that name — Section 1's
  field meanings are inferred from real bytes, not decompiled directly.
- `CCampaignGameFileHeader`'s trailing three u32s (guessed: difficulty/act/chapter) have no traced
  accessor confirming their meaning.
- The `u32 = 0` field between the DLC list and the embedded `.fcb` blob — present and measured,
  purpose unknown.
- Screenshot pixel channel order (RGBA vs BGRA) — not distinguished from the one sample checked.
- Whether the three Section-1 floats are really `PlayerPos` — plausible but not cross-checked against
  a live `-load`-and-read-back test or a decompiled accessor.
- ~19% of distinct hashes in the sample save (203 of 1,046) still don't resolve to any string found in
  the portion of the binaries' string tables scanned so far.
- Whether the four-section container layout is identical for quicksaves, manual saves, and checkpoint
  autosaves — only one save file was inspected byte-for-byte.
- What decides whether a given entity gets a persisted record at all — only the read side
  (`RestoreEntity`) was traced; "only entities that changed state get persisted" is carried over from
  community/developer-sourced theory, not independently re-derived.
- Which specific fields each entity class's own `RegisterProperties` captures — the mechanism is
  confirmed, but the 300+ anonymous `RegisterProperties` functions weren't individually attributed to
  class names.
- The entity-spawn path upstream of `CGhostManager::OnFinalize` (where `entitylibrary.fcb` is actually
  read to build a fresh entity) wasn't retraced from this angle — see [`.fcb`](./fcb.md) and
  [archives](./archives-fat-dat.md) for the general asset-loading path.

## Entity-Library Overlap

A direct follow-up to the mechanism above: which specific `entitylibrary.fcb` classes and fields does
`RestoreEntity`'s overlay actually touch in a real save? Answered by exporting one real save's full
`PersistenceDB` tree (via JackAll's Saves tab, which renders it in the same `type="..."`/`name="..."`
shape as an ordinary resolved `.fcb`) and cross-referencing every `<object type="X">` whose `X` is a
real `binary_classes.xml` class name against its child `<value name="Y">` names, by plain string
equality — no hash-matching needed once both sides share the same rendered shape.

An entity always spawns first from whatever `entitylibrary.fcb` currently says — edits apply
immediately and fully, unconditionally, as the starting point for every entity. Only if that specific
entity instance already has a `PersistenceDB` record does `RestoreEntity` run afterward and overlay the
specific properties captured on top of the freshly-spawned object. So the classes/fields below aren't
"unsafe to mod" in general — they're the specific set of properties that, for an entity a player has
already touched in an existing save, keep showing that save's frozen value until the record is cleared
or a fresh instance spawns. A new game, or any entity nobody has interacted with, always gets the edit.

### Headline numbers (one real save)

- **237 distinct real `entitylibrary.fcb` classes** appear instantiated inside `PersistenceDB` — every
  one a genuine `binary_classes.xml` class, reused verbatim because it's the same live C++ component
  tree being serialized.
- **574 distinct real `entitylibrary.fcb` member names** show up captured as per-instance state under
  those classes.
- For comparison, the save's `PersistenceDB` tree has 474 distinct object-type names and 1,319 distinct
  field names overall — so the `entitylibrary.fcb`-overlapping subset is roughly half by class count,
  well under half by field count. The rest is the save's own disjoint wrapper/computed-state vocabulary
  (`Tag_*` structural tags, AI runtime state like `ThreatLevel`/`AlertLevel`), which has no
  design-time counterpart to override in the first place.

### High-impact classes for modders

| Area | Class(es) | What's frozen for an already-touched instance |
|---|---|---|
| Vehicle handling/tuning | `CVehicle` + `Reliability`/`Sound`/`WheeledParams`/`Steering`/`Rumble`/`Gear0-2`/`SoundSettings`/`ParticleSettings`/`GaugesSettings`/`VehicleLightSettings`/`DustParticles` | Almost every tunable vehicle parameter — the single biggest risk category: `fAccelerationPushFactor`, collision-speed thresholds, `iRepairSteps`, `vehicleMaxLookAngle`, full gearbox/suspension/reliability-curve tuning |
| AI movement/behavior tuning | `CGameAgent`, `CPawnAgent`, `Body` | Run/sprint/walk speeds, accelerations/decelerations, `fJumpHeight`, swim speed, AI-sense thresholds |
| Visuals | `CGraphicComponent` (705 instances) | `bCastShadow`, `bReceiveShadow`, `fLODSphereRadius`, `hidMeshName`, `objModel`, LOD/reflection/ambient flags |
| Physics | `CCompoundPhysComponent`/`CRigidPhysComponent`/`CStaticPhysComponent`/`CVehicleWheeledPhysComponent` | Collision flags (`bUseFastCollision`, `bCreateAsStatic`), resource refs |
| Starting loadout | `Inventory` (31 instances) | `bAutoDraw`, `bAutoReload`, `bUnlimitedAmmo`, `packInventoryPack`, `sInitialWeaponCategory` |
| Pickup availability | `CPickupWeapon`/`CPickupDiamond`/`CPickupHealth`/`CPickupMissionItem`/`CMedicStation`/`CPickupPile`/`COpeningPickup` | `bPickable`, `bCanBeScouted` (and `CPickupWeapon` also `iMaxAmmo`/`iMinAmmo`) |
| Near-universal | almost every `C*Component` | `hidHasAliasName`, `Enabled`/`bEnabled`/`Enable`-shaped toggles, `Category`/`Name` |

**Good news for weapon balance mods**: `CFCXWeapon` (the live weapon-instance component) only overlaps
on `iAnimationValue`. None of the ballistics classes (`CWeaponFireBulletProperties`,
`CWeaponFireProjectileProperties`, `CWeaponPropertiesCommon`, ...) appear captured at all in this save.
Damage/range/reliability-curve balance values are archetype-only, always read fresh at spawn — safe to
edit even for a save where that weapon's already been picked up.

### Per-class breakdown

Every real `entitylibrary.fcb` class found instantiated in this save, most common first, with its
captured field list split into design-time members (a real `binary_classes.xml` field, frozen for a
touched instance) and savegame-only fields (dynamic/computed per-instance state with no design-time
counterpart — not a modding concern in the same sense). Measured from one save (73,282 `<object>`
elements, 474 distinct type names, 1,319 distinct field names) against `binary_classes.xml` (2,025
class names, 2,009 member names).

#### All 237 real `entitylibrary.fcb` classes found instantiated, most common first

**`enum`** (×2185)
- design-time fields also captured as instance state: `Value`

**`LayerId`** (×1573)
- design-time fields also captured as instance state: `Category`, `Name`

**`Components`** (×1260)

**`State`** (×1203)
- design-time fields also captured as instance state: `Enabled`, `Name`, `bEnabled`, `disIndex`, `hidNodeType`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentLoop`, `CurrentSequence`, `Duration`, `GroupOwner`, `HasEnemyBeenInsideRegion`, `HasSentEngagedMessage`, `HasSentFailureMessage`, `HasSentSuccessMessage`, `InverseSync`, `IsEnabled`, `IsPaused`, `IsPlayerInRegion`, `LoopTimePercent`, `NearZOverride`, `NonScaledDuration`, `Player`, `RequestedMoveStateID`, `ScriptedEventCollisionGroup`, `ScriptedSceneStarted`, `SocialRegionState`, `Start`, `Started`, `SyncEntity`, `TargetID`, `Visible`, `WorldMatrix`, `bBroadcastEnabled`, `bHasBeenSpawned`, `bIsGhosted`, `bPlayerInside`, `bSupplies`, `bTacticals`, `bVehicles`, `isFromArchetype`, `strState`

**`CEventComponent`** (×1181)
- design-time fields also captured as instance state: `hidHasAliasName`

**`CPersistComponent`** (×1137)
- design-time fields also captured as instance state: `hidHasAliasName`, `selLevel`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `OriginalSector`

**`Links`** (×1077)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Loop`

**`Children`** (×850)

**`BindingHierarchy`** (×723)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `ForceSpawn`

**`Root`** (×723)
- design-time fields also captured as instance state: `BoneId`, `EntityId`, `LogicalBind`, `MeshIndex`

**`CGraphicComponent`** (×705)
- design-time fields also captured as instance state: `agAmbientGroup`, `bAllowCullBySize`, `bAlwaysShowInReflection`, `bBehaveLikeAPickup`, `bCastAmbientShadow`, `bCastShadow`, `bIntelHackGliderOn`, `bOverrideLODSphere`, `bReceiveShadow`, `bShowInReflection`, `fLODSphereRadius`, `hidComponentClassName`, `hidGroundColor`, `hidHasAliasName`, `hidHasAmbientValues`, `hidHeightAbove`, `hidIndex`, `hidMeshName`, `hidObjectHeight`, `hidSkyOcclusion0`, `hidSkyOcclusion1`, `hidSkyOcclusion2`, `hidSkyOcclusion3`, `objModel`, `olgLightGroup`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `RenderInNearZViewPortID`, `VisibilityNodes`

**`Resource`** (×583)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `ResId`, `TypeId`

**`CFileDescriptorComponent`** (×576)
- design-time fields also captured as instance state: `fileName`, `hidDescriptor`, `hidHasAliasName`

**`Description`** (×430)
- design-time fields also captured as instance state: `disEntityId`, `entShape`, `fHeight`, `fWidth`, `hidAngles`, `hidConstEntity`, `hidEntityClass`, `hidName`, `hidPos`, `hidPos_precise`, `hidResourceCount`, `texTexture`, `tplCreatureType`, `vColor`

**`CSoundComponent`** (×394)
- design-time fields also captured as instance state: `hidHasAliasName`, `sndptSoundPoint`

**`Intel`** (×392)
- design-time fields also captured as instance state: `selIntelType`, `vPos`

**`CObjectSoundAndFXComponent`** (×366)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `hidCanPlayFallingSound`, `hidLastVelocity`

**`CCountersComponent`** (×339)
- design-time fields also captured as instance state: `archStimEffectTable`, `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Invincible`, `StimImmunity`

**`CMissionComponent`** (×298)
- design-time fields also captured as instance state: `ForceMerge`, `hidCategory`, `hidHasAliasName`, `hidMissionLayerPath`

**`CParticleFXComponent`** (×286)
- design-time fields also captured as instance state: `hidHasAliasName`

**`Effects`** (×285)

**`CCompoundPhysComponent`** (×270)
- design-time fields also captured as instance state: `bAnimateable`, `bAnimatedControlPos`, `bCreateAsStatic`, `bUseFastCollision`, `bUseMaxTerrainSlope`, `fSelfCollOverrideSpeed`, `hidHasAliasName`, `hidHasStatic`, `hidNodeType`, `hidResourceId`, `sndExitGroupSound`, `sndExitSound`, `sndtpSoundType`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CollisionSystemGroup`, `Enable`, `PartType`, `Velocity`

**`RootNode`** (×270)
- design-time fields also captured as instance state: `FirstStateIndex`, `disName`, `hidBoneIndex`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentState`

**`CTriggerComponent`** (×266)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Enable`

**`CIgnitorComponent`** (×232)
- design-time fields also captured as instance state: `Flags`, `hidHasAliasName`, `stimIgniteId`, `stimIgniteIdMP`

**`CMapElementComponent`** (×225)
- design-time fields also captured as instance state: `selState`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Dirty`, `DirtyTime`, `Discovered`

**`enumIntelType`** (×196)

**`Node`** (×185)
- design-time fields also captured as instance state: `Pos`, `index`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentHealth`, `Velocity`

**`CSimpleAnimationComponent`** (×178)
- design-time fields also captured as instance state: `fileSkeleton`, `hidHasAliasName`, `sPartName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Enable`, `RootTransform`

**`ActivePartOverwrite`** (×168)
- design-time fields also captured as instance state: `ColorIndex`, `PartID`, `TextureIndex`

**`CRigidPhysComponent`** (×150)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CollisionSystemGroup`, `CurrentHealth`, `Enable`, `GraphicMatrixToIdentity`, `PartType`, `Velocity`

**`CBindingComponent`** (×146)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `LocalAngles`, `LocalPos`, `PrecisePosition`

**`CAIShootMeObject`** (×144)

**`CCompoundPhysNetworkComponent`** (×141)

**`hidBone`** (×140)
- design-time fields also captured as instance state: `hidIndex`

**`CPhysNetworkComponent`** (×136)

**`CProximityTriggerComponent`** (×132)
- design-time fields also captured as instance state: `Usable`, `bEnabled`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentUniqueUserId`

**`Child`** (×127)
- design-time fields also captured as instance state: `BoneId`, `EntityId`, `LogicalBind`, `MeshIndex`

**`Entity`** (×102)

**`NodeList`** (×88)

**`CScriptCallbackComponent`** (×87)

**`Part`** (×79)
- design-time fields also captured as instance state: `Name`, `bImpulseOnDetach`, `bKeepAttached`, `fCollisionExtraRadius`, `fFloatingScale`, `fHealth`, `fWaterFriction`, `hidIsFrame`, `hidTypeNameIndex`, `nStartStateIndex`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentHealth`, `StateIndex`

**`CStaticPhysComponent`** (×79)
- design-time fields also captured as instance state: `bAnimateable`, `bIgnoreInExplosions`, `bLargeEntity`, `bUseMaxTerrainSlope`, `hidHasAliasName`, `hidResourceId`, `hidResourceIndex`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CollisionSystemGroup`, `Enable`, `GraphicMatrixToIdentity`, `PartType`, `Velocity`

**`CIgnitorNetworkComponent`** (×74)

**`CPickupDiamond`** (×70)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `User`, `bActive`, `bOpened`, `fCloseCounter`, `fOpenCounter`, `fTimeSinceOpen`

**`hidLinks`** (×65)

**`CTimeOfDayTriggerComponent`** (×61)
- design-time fields also captured as instance state: `bEnabled`

**`CCustomMaterialComponent`** (×56)

**`object`** (×51)
- design-time fields also captured as instance state: `hidDetailObject`, `hidIndex`, `hidMeshName`, `hidNodeName`, `hidNodeNameLOD0`, `objModel`

**`CDoor`** (×37)
- design-time fields also captured as instance state: `bEnabled`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentDoorUser`, `LastDoorAction`, `UsedOnce`

**`CSafeHouseComponent`** (×37)
- design-time fields also captured as instance state: `bLocked`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Discovered`, `NeverDeleteCount`

**`Ghost`** (×34)
- design-time fields also captured as instance state: `bIsEnabled`, `fSpeed`

**`hidState`** (×33)
- design-time fields also captured as instance state: `hidGraphicIndex`, `hidHighresRigidbodyName`, `hidPartId`, `hidRigidbodyIndex`, `hidRigidbodyName`

**`CDynamicDeploadComponent`** (×33)
- design-time fields also captured as instance state: `hidHasAliasName`

**`Inventory`** (×31)
- design-time fields also captured as instance state: `archGPSVehicleArchetype`, `bAutoDraw`, `bAutoReload`, `bUnlimitedAmmo`, `packInventoryPack`, `sInitialWeaponCategory`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentGadget`, `CurrentWeapon`, `CurrentWeaponEntity`, `DesiredGadget`, `DesiredTrack`, `DesiredWeapon`, `EquippedTrack`, `GPSEntity`, `LastGadget`, `LastWeapon`, `Locked`, `ThrowGadget`, `UseExternalWeapon`

**`IntelData`** (×30)

**`CFCXAIComponent`** (×29)
- design-time fields also captured as instance state: `Type`, `hidHasAliasName`

**`AIObject`** (×29)
- design-time fields also captured as instance state: `Enabled`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AiShootMeObjectId`, `AimStrategy`, `AlertLevel`, `AlertLostTargetRushType`, `AllSocialRegionType`, `AutomaticScriptedScenePrefab`, `BlindCombatLevel`, `BulletJustMissed`, `BumpAngle`, `BumpSpeed`, `CachedAnchorOrientation`, `CachedAnchorPosition`, `ClearVisibility`, `CurrentArmyMemberRole`, `CurrentArmyMemberRoleAction`, `CurrentArmyMemberState`, `CurrentAttackZone`, `CurrentBuildingId`, `CurrentVehicleMemberState`, `DesiredArmyMemberRole`, `DesiredArmyMemberRoleAction`, `Destination`, `EmotionStrategy`, `FlagField`, `FlareCooldown`, `FuzzyVisibility`, `GotIntuition`, `GotoFireRange`, `HealthFailureWhileHealing`, `HighestSocialRegionType`, `InitialReinforcementRegionId`, `InitialStrategicZoneId`, `IntuitionTimer`, `IsDead`, `IsInDesert`, `IsPlayer`, `IsPlayerInAIvsAIZone`, `IsPrimarySlotRunning`, `IsReady`, `IsSafeHouseMerc`, `IsSecondarySlotRunning`, `IsSpecialMissionBehaviourMerc`, `IsUsingMountedWeapon`, `JustStarted`, `LastBlindCombatNotification`, `LastMuzzleFlashTime`, `LookStrategy`, `MercBrain`, `MercBrainST`, `MoveCallbackLayer`, `MustDieNow`, `PillarThresholdCross`, `PreviousArmyMemberState`, `ProjEscapeType`, `ReadyForMoveCallback`, `RescueAttempt`, `RescueCooldown`, `RescueSafe`, `RescueState`, `Reserved`, `ReservedEntrance`, `RunOverSoundPlayed`, `SawSomethingLevel`, `ShineLensCounter`, `SpecialStrategy`, `ThreatLevel`, `ThreatLevelCounter`, `ThreatLevelTimeCounter`, `ThreatPriority`, `ThresholdLevel`, `TimeSinceHMRFailure`, `TimeSinceLastShot`, `UserRolePriority`, `VariationID`, `VariationID2`, `VehicleFallBackPositions`, `WagerHandle`, `WeaponCurrentClass`, `WeaponLastTransitionTime`, `WeaponPreviousClass`, `WeaponSwitchTo`

**`Sound`** (×28)
- design-time fields also captured as instance state: `fWheelsEnterWaterFadeoutTime`, `mixInVehicleSoundPreset`, `sndBrake`, `sndEngineIgnition`, `sndEngineLoop`, `sndExtraTorqueEngineLoop`, `sndFrameLoop`, `sndGearShift_MajorDamage`, `sndGearShift_MinorDamage`, `sndGearShift_New`, `sndId`, `sndPlayEngineIdleLoop`, `sndStopEngineIdleLoop`, `sndSuspensionSoundHeavy`, `sndSuspensionSoundMedium`, `sndSuspensionSoundSmall`, `sndThrustPedal`, `sndTurnOffEngine`, `sndWheelRoll_1`, `sndWheelRoll_2`, `sndWheelRoll_3`, `sndWheelRoll_4`, `sndWheels_EnterWater`, `sndWheels_RunOver`, `sndmlWheelSlipSoundMultilayer`

**`hidStateFX`** (×28)
- design-time fields also captured as instance state: `hidFXNode`

**`CCharacterPhysComponent`** (×23)
- design-time fields also captured as instance state: `Enabled`, `LockBone`, `RagdollCollideSpeedLimit`, `fileRagdoll`, `hidHasAliasName`, `hidResourceId`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CanPushObjects`, `CollisionSystemGroup`, `Driver`, `Enable`, `Gravity`, `OverriddenCollision`, `PartType`, `PhysicsEnabled`, `Stance`, `Velocity`

**`CAnimationComponent`** (×23)
- design-time fields also captured as instance state: `fileFacialFile`, `fileSkeleton`, `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `BackupGravity`, `Enable`, `ForceDisplacement`, `ForceDisplacementFactor`, `PhysWeight`, `RagdollController`

**`Stim`** (×22)
- design-time fields also captured as instance state: `bBurnStim`, `bCrushStim`, `bFalloff`, `bPierceStim`, `eventMask`, `fBulletImpulseScale`, `fExplosionImpulseScale`, `fRadius`, `hidEventName`, `hidShowRadius`, `hidShowType`, `hidTargetEntityId`, `nFalloffMinLevel`, `nLevel`, `sDetail`, `selType`

**`ReinforcementRegion`** (×20)
- design-time fields also captured as instance state: `iMercDensity`, `iMercDensityThreshold`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `ReinforcementCounter`

**`Sounds`** (×17)

**`CWeaponNetworkComponent`** (×16)

**`CFCXWeapon`** (×16)
- design-time fields also captured as instance state: `iAnimationValue`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AmmoInClip`, `AutoReload`, `AutoReloadOnUnequip`, `ConsecutiveShots`, `CurrentBulletSpread`, `Indestructible`, `JamCounter`, `JammingBullet`, `MaxReliability`, `OwnerId`, `PreparedForUse`, `RefillAmmoAfterNextEquip`, `RememberedAmmoInClip`, `RememberedAmmoOverflow`, `WielderID`

**`FireStrategy`** (×16)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `BackfireCounter`, `BackfireStimCounter`, `BeingUsed`, `CastShadow`, `LoadedProjectile`, `PlayingMalfunctionSound`, `ProjStatus`, `ShootBoneIndex`, `UserID`

**`CPawn`** (×16)
- design-time fields also captured as instance state: `Enabled`, `IsUsableOrientationNeeded`, `Usable`, `bIsAI`, `filePawnStateMachine`, `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `JumpHeight`, `SavedMoveState`

**`Skills`** (×16)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AllowCameraOffset`, `AngularSpeed`, `CameraOffsetBlendTime`, `Diving`, `HeadUnderwater`, `LookSensitivity`, `LookSensitivityIronSight`, `MentalState`, `Sliding`, `Swimming`, `WantToShootHMR`

**`CPawnBeautifierComponent`** (×16)
- design-time fields also captured as instance state: `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `ApplyDisplacement`, `Enable`

**`CPawnAgent`** (×16)
- design-time fields also captured as instance state: `bHasALongRangeWeapon`, `bOppositeArmy`, `m_AlertClearVal`, `m_AlertFuzzyVal`, `m_CombatClearVal`, `m_CombatFuzzyVal`, `m_DeadClearVal`, `m_DeadFuzzyVal`, `m_IdleClearVal`, `m_IdleFuzzyVal`, `m_SocialClearVal`, `m_SocialFuzzyVal`, `m_SpecialClearVal`, `m_SpecialFuzzyVal`, `m_ThresholdClearVal`, `m_ThresholdFuzzyVal`, `m_VehicleClearVal`, `m_VehicleFuzzyVal`, `selAIInfamyMode`, `selArmy`, `selODU`, `selSpecialCharacterType`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AIInfamyValue`, `AIStressLevel`, `BuddyDownEnable`

**`ShootingSystem`** (×16)
- design-time fields also captured as instance state: `archGroupNumberCurve`, `fMissHeight`, `fMissWidth`, `fPointBlankDistance`, `fTimerToMissTarget`, `fTimerToPointBlank`

**`SensorySystem`** (×16)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `FocusFilter`

**`CFrankensteinComponent`** (×16)
- design-time fields also captured as instance state: `bCheatKnees`, `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Enable`, `ScriptEventOverrideID`

**`Reliability`** (×15)
- design-time fields also captured as instance state: `MajorDamageEngineScale`, `MajorDamageEngineStartTime`, `MajorDamageLevel`, `MinorDamageEngineScale`, `MinorDamageEngineStartTime`, `MinorDamageLevel`, `MintEngineStartTime`, `fInitialReliability`, `sndswtpReliabilitySoundSwitchType`, `sndswvlBrokenSoundSwitchValue`, `sndswvlMajorDamageSoundSwitchValue`, `sndswvlMinorDamageSoundSwitchValue`, `sndswvlNoDamageSoundSwitchValue`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentValue`, `LastInstigator`, `MaxValue`, `SendChangedEvents`

**`hidStates`** (×15)

**`hidStateFXs`** (×15)

**`CGraphicKitComponent`** (×15)

**`PartOverwrite`** (×15)

**`Health`** (×15)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentValue`, `LastInstigator`, `MaxValue`, `SendChangedEvents`

**`CAISoundAndFXComponent`** (×15)
- design-time fields also captured as instance state: `fFootstepsAudibleDistance`, `hidHasAliasName`, `logmatFakeMulletMaterial`, `matimpFakeBulletFx`, `matimpFootStepThird`, `matimpLanding`, `psDiveMove`, `psEmerge`, `psStorm`, `psSubmerge`, `psSwimIdleChest`, `psSwimIdleHands`, `psSwimMoveChest`, `psSwimMoveHands`, `sndLandingFatalSoundID`, `sndswtpFootstepSpeedSwitchType`, `sndtpLandingFatalSoundType`

**`IgnitorStims`** (×15)
- design-time fields also captured as instance state: `bIgniteOnBurn`, `bIgniteOnCrush`, `bIgniteOnPierce`

**`CRelayTriggerComponent`** (×14)
- design-time fields also captured as instance state: `bEnabled`

**`CGadgetNetworkComponent`** (×13)

**`CGadget`** (×13)
- design-time fields also captured as instance state: `iAnimationValue`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `OwnerId`, `UnlimitedUse`, `Uses`, `WielderID`

**`UseStrategy`** (×13)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `ArrowsVisible`, `Attached`, `BeingUsed`, `ChangingLevel`, `CompassEntity`, `CurrentActionMap`, `CurrentColor`, `IndexKeyLocation`, `LastSector`, `MapDesired`, `MapType`, `Message`, `MessageDelayCount`, `MonocularEntity`, `MonocularEquipped`, `PendingPhoneCall`, `PendingStartId`, `PlayerMarkerEntity`, `Playing`, `RingPauseCount`, `RingTriesCount`, `Ringing`, `SavedMapTexture`, `SpawnedProjID`, `Throw`, `UserID`, `sndStart`

**`Length`** (×12)
- design-time fields also captured as instance state: `Value`

**`CPickupWeapon`** (×12)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`, `iMaxAmmo`, `iMinAmmo`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `bActive`, `iAmmoCount`

**`CPickupNetworkComponent`** (×12)

**`enumNextState`** (×12)

**`ParticleSystem`** (×11)
- design-time fields also captured as instance state: `bFollowEntity`, `disFXName`, `psEmitter`

**`CDominoComponent`** (×11)
- design-time fields also captured as instance state: `fileBoxPath`, `hidHasAliasName`, `hidStartOnLoad`

**`CMapIntelligence`** (×9)
- design-time fields also captured as instance state: `bDisplayOnMap`, `fMarkerZ`, `hidHasAliasName`, `selType`, `vInitialPos`

**`CFCXCountersComponentAI`** (×8)
- design-time fields also captured as instance state: `bIsInvincibleExceptToPlayer`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `FirePropagationResistantTimer`, `Invincible`, `StimImmunity`

**`COcclusionQueryComponent`** (×8)

**`CPositionLoggerComponent`** (×8)
- design-time fields also captured as instance state: `LoggingSize`, `distanceInterval`, `hidHasAliasName`, `timeInterval`, `useDistanceInterval`

**`CDynamicLightComponent`** (×8)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Enable`

**`CEntitySpawner`** (×8)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `bAlreadySpawned`

**`CPickupMissionItem`** (×7)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `bActive`, `bNeverPicked`

**`CFCXCountersComponentAIBuddy`** (×7)
- design-time fields also captured as instance state: `WeaponJamProbabilityScale`, `archStimEffectTable`, `bEnableHitLocations`, `bIsInvincibleExceptToPlayer`, `bIsInvincibleToAI`, `bIsInvincibleToPlayer`, `fAgentHealth`, `fHealthFailureCantDieDuration`, `fHealthFailureLimbsHitModifier`, `fHealthFailureTorsoHitModifier`, `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `FirePropagationResistantTimer`, `Invincible`, `IsBuddyDownActive`, `SmokeStarted`, `StimImmunity`

**`CCorpseComponent`** (×7)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `StartInactive`, `StartRagdoll`

**`StateSubNode`** (×6)
- design-time fields also captured as instance state: `bExplodes`, `bSoundMustFollowEntity`, `bStartEffectsOnCreate`, `disName`, `fFloatingScale`, `fHealth`, `fWaterFriction`, `hidGraphicIndex`, `hidResIndex`, `sndExitGroupSound`, `sndExitSound`, `sndInitSound`, `sndtpSoundType`, `vectorCenterOfMassOffset`

**`hidEffectBones`** (×6)

**`OnDamage`** (×6)
- design-time fields also captured as instance state: `selNextState`

**`OnEvent`** (×6)
- design-time fields also captured as instance state: `bExplodes`, `bSoundMustFollowEntity`, `bTriggerEffects`, `selNextState`, `sndExitGroupSound`, `sndExitSound`, `sndtpSoundType`

**`CPhysPhantomComponent`** (×6)

**`Parts`** (×4)

**`CVehicle`** (×4)
- design-time fields also captured as instance state: `HideBodyYawAngle`, `bDisableEnterCollisionDetection`, `bDiscardAfterUse`, `bUseExitPointOffset`, `driverActionMap`, `fAccelerationPushFactor`, `fBigCollisionSpeed`, `fDirtFactor`, `fDustFactor`, `fEngineUnderWaterZOffset`, `fExitPointOffset`, `fIncomingFireEvasiveness`, `fJumpOutBrakeFactor`, `fJumpOutMinSpeed`, `fKickForce`, `fMediumCollisionSpeed`, `fMinCollisionSpeed`, `fSeatEntryMaxRadius`, `fUnderWaterMaxDepth`, `fWindFactor`, `hidHasAliasName`, `iAnimVehicleType`, `iBailoutCrushStimLevel`, `iRepairSteps`, `matimpBigCollisionImpact`, `matimpMediumCollisionImpact`, `matimpSmallCollisionImpact`, `nMaxRandomColorIndex`, `nMinRandomColorIndex`, `sEnterSignal`, `sEnterUsageString`, `sKickUsageString`, `sLeaveTransitionSignal`, `sName`, `sRepairUsageString`, `selVehicleColor`, `selVehicleType`, `sndKickSoundID`, `sndtpKickSoundType`, `vehicleMaxLookAngle`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AllowHeadLights`, `CanExplode`, `CanTriggerExplosion`, `CurrentHealth`, `CurrentParticle`, `CurrentSound0`, `CurrentSound1`, `EnableUse`, `EngineState`, `GaugeRPMBaseRot`, `GaugeRPMBoneIndex`, `GaugeRPMBoneName`, `GaugeSpeedBaseRot`, `GaugeSpeedBoneIndex`, `GaugeSpeedBoneName`, `HandBrake`, `Velocity`, `VelocityOverrideEnabled`, `nInstantExplosionCrushHealth`

**`CVehicleNetworkComponent`** (×4)
- design-time fields also captured as instance state: `fDisabledResetTime`, `fEmptyResetTime`, `fPawnsLookingRadius`, `fPawnsTooCloseRadius`, `hidHasAliasName`

**`CGrassDisplacementComponent`** (×4)
- design-time fields also captured as instance state: `hidHasAliasName`

**`CVehicleMaterialComponent`** (×4)
- design-time fields also captured as instance state: `fDirtFactor`, `fDustFactor`, `hidHasAliasName`, `nVehicleColor`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Destroyed`

**`Wheel`** (×4)
- design-time fields also captured as instance state: `bDriving`, `bHandBrake`, `fBrakingTorque`, `fMass`, `fSuspDampingCompression`, `fSuspDampingRelaxation`, `fSuspLength`, `fSuspStrength`, `fWheelOffsetZ`, `nSurfaceIdx`

**`hidPrimitive`** (×4)

**`hidPart`** (×4)
- design-time fields also captured as instance state: `hidGraphicIndex`, `hidHighresRigidbodyName`, `hidPartId`, `hidRigidbodyIndex`, `hidRigidbodyName`

**`Offset`** (×4)
- design-time fields also captured as instance state: `Value`

**`enumLevel`** (×4)

**`CVehicleWheeledPhysComponent`** (×3)
- design-time fields also captured as instance state: `fCollisionImmunityDelay`, `fFloatingScale`, `fMaxFallingDist`, `fMinFallingDist`, `fWaterFriction`, `hidHasAliasName`, `hidNewCollision`, `hidResourceId`, `matimpWheelDustFx`, `nMaxFallingCrushLevel`, `nMaxStimCollisionLevel`, `nMinFallingCrushLevel`, `sndtpSoundType`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CollisionSystemGroup`, `CurrentHealth`, `Driver`, `Enable`, `EngineStartTimer`, `HealthDamageEnabled`, `PartType`, `Velocity`

**`WheelSuspLength`** (×3)

**`CVehicleSoundAndFXComponent`** (×3)
- design-time fields also captured as instance state: `hidHasAliasName`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `hidCanPlayFallingSound`, `hidLastVelocity`

**`CLiquidPropaneTank`** (×3)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Destroyed`, `PierceDamageCumulatedSoFar`, `ReceivedAPierceStimBefore`

**`Seat`** (×3)
- design-time fields also captured as instance state: `EntryBoneName`, `MaxLookAngle`, `MinLookAngle`, `bAIUserType`, `bHumanUserType`, `bMultiUserType`, `sSeatBoneName`

**`FocusFOV`** (×3)
- design-time fields also captured as instance state: `fAngle`, `fLength`

**`PeripheralFOV`** (×3)
- design-time fields also captured as instance state: `fAngle`, `fLength`

**`COpeningPickup`** (×3)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `User`, `bActive`, `bOpened`, `fCloseCounter`, `fOpenCounter`, `fTimeSinceOpen`

**`CMagicCrate`** (×3)

**`CPickupPile`** (×3)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Destroyed`, `bActive`

**`CPickupPileNetworkComponent`** (×3)

**`CRandomShooterComponent`** (×3)

**`CFCXCompassObjectives`** (×3)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentRangeIndex`, `CurrentRangeX`, `CurrentRangeY`, `User`

**`Objectives`** (×3)

**`CMountedWeaponNetworkComponent`** (×2)

**`CMountedWeapon`** (×2)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `IsUsable`, `PivotRotation`, `bOverheated`, `controllerPhysState`, `fCoolDownCounter`, `fCurrentHeat`, `iCurrentState`, `userID`, `weaponID`

**`CRadio`** (×2)

**`enumType`** (×2)

**`Primitive`** (×2)
- design-time fields also captured as instance state: `bBreaksGrass`, `fLength`, `fWidth`, `selPrimitiveType`, `vPosition`

**`enumPrimitiveType`** (×2)

**`CAgent`** (×2)
- design-time fields also captured as instance state: `Brain`, `aiwsBrainWorkspace`

**`CGameAgent`** (×2)
- design-time fields also captured as instance state: `bIsScripted`, `fAccelerationsFast`, `fAccelerationsNormal`, `fAccelerationsSlow`, `fDecelerationsFast`, `fDecelerationsNormal`, `fDecelerationsSlow`, `fSpeedsBabyStep`, `fSpeedsJog`, `fSpeedsRun`, `fSpeedsSprint`, `fSpeedsWalk`, `fVariationBabyStep`, `fVariationJog`, `fVariationRun`, `fVariationSprint`, `fVariationWalk`

**`DensityManagement`** (×2)
- design-time fields also captured as instance state: `bLastToBeDeleted`, `bNeverDelete`

**`FootstepSpeedSwitch`** (×2)
- design-time fields also captured as instance state: `fSpeedHigherBound`, `sndswvlFootstepSwitchValue`

**`CVisibilityOcclusionVolumeComponent`** (×2)
- design-time fields also captured as instance state: `fKillDistance`, `hidHasAliasName`, `hidShapeType`, `vectorSize`

**`CRoadSign`** (×2)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentTag`

**`Damage`** (×2)
- design-time fields also captured as instance state: `bDamageable`, `bPlayerOnly`

**`States`** (×2)

**`CMedicStation`** (×2)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `User`, `bActive`, `bOpened`, `fCloseCounter`, `fOpenCounter`, `fTimeSinceOpen`

**`CMedicStationNetworkComponent`** (×2)

**`TimeOfDay`** (×1)

**`CVehicleFloatingPhysComponent`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CollisionSystemGroup`, `CurrentHealth`, `Driver`, `Enable`, `EngineStartTimer`, `HealthDamageEnabled`, `PartType`, `Velocity`

**`DlcSwitch`** (×1)
- design-time fields also captured as instance state: `vectorDlcSwitchAnglesOffset`, `vectorDlcSwitchPosOffset`

**`WheeledParams`** (×1)
- design-time fields also captured as instance state: `fChassisUnitInertiaPitch`, `fChassisUnitInertiaRoll`, `fChassisUnitInertiaYaw`, `fEnginePower`, `fExtraClimbEnginePower`, `fExtraTorqueFactor`, `fGearBoxTopSpeed`, `fGroundFrictionReduceMaxAngle`, `fGroundFrictionReduceMinAngle`, `fMass`, `fRearWheelHandBrakeFrictionScale`, `fTorquePitchFactor`, `fTorqueRollFactor`, `fTorqueYawFactor`, `nGears`, `vectorCenterOfMassOffset`

**`Steering`** (×1)
- design-time fields also captured as instance state: `bUseTimedSteering`, `fHighDirectMaxAngle`, `fHighMaxAngle`, `fHighSteerSpeed`, `fLowDirectMaxAngle`, `fLowMaxAngle`, `fLowSteerSpeed`, `fMaxSpeed`

**`Wheels`** (×1)

**`hidWheelPrimitives`** (×1)

**`DlcSwitchWheelSuspOffset`** (×1)

**`MountedWeapons`** (×1)

**`MountedWeaponEntry`** (×1)
- design-time fields also captured as instance state: `archMountedWeapon`

**`POV`** (×1)
- design-time fields also captured as instance state: `vectorNeutral`, `vectorQ0`, `vectorQ1`, `vectorQ2`, `vectorQ3`, `vectorQ4`, `vectorQ5`, `vectorQ6`, `vectorQ7`

**`Leaning`** (×1)
- design-time fields also captured as instance state: `fCameraDisplacementFactor`, `fCameraRotationFactor`, `fSpeedRelevance`

**`Rumble`** (×1)
- design-time fields also captured as instance state: `fAmplitudeC`, `fAmplitudeVelocityFactor`, `fFrequency`, `fLeanFactor`, `fStrength`

**`enumVehicleColor`** (×1)

**`FOV`** (×1)
- design-time fields also captured as instance state: `archFOVCurveName`, `fFOVAngle`, `fFOVTransitionTime`

**`PassengerSeatsLookAngles`** (×1)

**`UserSeatTypeOverride`** (×1)

**`enumVehicleType`** (×1)

**`EngineDamaged`** (×1)
- design-time fields also captured as instance state: `psBrokenEngineSmoke`, `psBrokenEngineSmokeNoHood`, `psEngineSmoke`, `psEngineSmokeNoHood`

**`EngineFire`** (×1)
- design-time fields also captured as instance state: `fFireDelay`, `psEngineFire`, `psEngineFireNoHood`

**`EngineFireStim`** (×1)
- design-time fields also captured as instance state: `bFalloff`, `eventMask`, `fRadius`, `hidEventName`, `hidShowRadius`, `hidShowType`, `hidTargetEntityId`, `nFalloffMinLevel`, `nLevel`, `sDetail`

**`EngineExplosion`** (×1)
- design-time fields also captured as instance state: `fExplosionDelay`, `fExplosionImpulse`, `nInstantExplosionCrushMaxHealth`, `nInstantExplosionCrushThreshold`, `psEngineExplosion`

**`Explosion`** (×1)
- design-time fields also captured as instance state: `ExplosionCenter`, `fPartsSpeed`, `vecSelfVelocity`

**`ExplosionStim`** (×1)
- design-time fields also captured as instance state: `bFalloff`, `eventMask`, `fPhysImpulse`, `fRadius`, `hidEventName`, `hidShowRadius`, `hidShowType`, `hidTargetEntityId`, `nFalloffMinLevel`, `nLevel`, `sDetail`

**`ExtraStims`** (×1)

**`SoundSettings`** (×1)
- design-time fields also captured as instance state: `sndEngineBurning`, `sndEngineExplosion`, `sndEngineMajorDamage`, `sndEngineMinorDamage`, `sndmlDamageSoundMultilayer`, `sndmlExtraTorqueSoundMultilayer`, `sndmlRPMSoundMultilayer`, `sndmlSpeedSoundMultilayer`, `sndmlThrustPedalSoundMultilayer`, `sndswtpMaterialSoundSwitchType`, `sndtpSoundType`

**`Settings`** (×1)
- design-time fields also captured as instance state: `fSuspensionHeavySpeed`, `fSuspensionMediumSpeed`, `fSuspensionSmallSpeed`, `fThrustPedalStopFadeOut`, `fThrustPedalStopThreshold`, `vectorEngineOffset`

**`GearEmulation`** (×1)

**`Gear0`** (×1)
- design-time fields also captured as instance state: `fMaxRPM`, `fMaxSpeed`, `fMinRPM`, `fMinSpeed`

**`Gear1`** (×1)
- design-time fields also captured as instance state: `fMaxRPM`, `fMaxSpeed`, `fMinRPM`, `fMinSpeed`

**`Gear2`** (×1)
- design-time fields also captured as instance state: `fMaxRPM`, `fMaxSpeed`, `fMinRPM`, `fMinSpeed`

**`ParticleSettings`** (×1)
- design-time fields also captured as instance state: `psFxExhaust`, `psFxWaterSplash`, `vectorFxWaterSplashOffset`

**`GaugesSettings`** (×1)
- design-time fields also captured as instance state: `fRPMMaxAngle`, `fSpeed50kmhAngle`, `fSpeedCutOff`, `fSpeedLowRangeAngle`, `fSpeedLowRangeSpeed`

**`VehicleLightSettings`** (×1)
- design-time fields also captured as instance state: `fBrakeLightDimmedFactor`, `fDynamicLightInnerAngle`, `fDynamicLightOuterAngle`, `fDynamicLightRange`

**`DustParticles`** (×1)
- design-time fields also captured as instance state: `fDustAvgEmissionDist`, `fDustLifeTimeRatio`, `fDustLifeTimeRatioMax`, `fDustLifeTimeRatioMin`, `fDustRandomDistance`, `fDustSizeRatio`, `fDustSizeRatioMax`, `fDustSizeRatioMin`

**`Impact`** (×1)
- design-time fields also captured as instance state: `bIsSingleDropImpactObject`, `fMinimalCollisionSpeed`, `fSpeedForMaxDropImpactVolume`, `matimpDropImpact`

**`MovementSound`** (×1)
- design-time fields also captured as instance state: `sndRollingSound`, `sndRollingSoundEnd`, `sndSlidingSound`, `sndSlidingSoundEnd`, `sndmlObjectMovementMultilayer`, `sndtpRollingSoundType`, `sndtpSlidingSoundType`

**`Falling`** (×1)
- design-time fields also captured as instance state: `bAllowAfterCollision`, `fSpeedToPlayFallingSound`, `sndFallingSound`, `sndtpFallingType`

**`Water`** (×1)
- design-time fields also captured as instance state: `fZSpeedForMaxSplashVolume`, `fZSpeedToTriggerSplash`

**`Primitives`** (×1)

**`CVehicleAgent`** (×1)
- design-time fields also captured as instance state: `bOnlyUsableByPlayer`, `fRunOverSoundRange`

**`ParticleSystems`** (×1)

**`CLookAtTriggerComponent`** (×1)
- design-time fields also captured as instance state: `bEnabled`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `InitalTestDone`, `InsideTerminalFOV`

**`CharacterParams`** (×1)
- design-time fields also captured as instance state: `bUpdateRotation`, `bUseRigidBased`, `fMass`, `fMaxSlope`, `fMaxTerrainSlope`

**`StandDimensions`** (×1)
- design-time fields also captured as instance state: `fStandCapsuleRadius`, `vecStandCapsulePointA`, `vecStandCapsulePointB`

**`CrouchDimensions`** (×1)
- design-time fields also captured as instance state: `fStandCapsuleRadius`, `vecStandCapsulePointA`, `vecStandCapsulePointB`

**`SwimDimensions`** (×1)
- design-time fields also captured as instance state: `fStandCapsuleRadius`, `vecStandCapsulePointA`, `vecStandCapsulePointB`

**`Body`** (×1)
- design-time fields also captured as instance state: `SwimmingClimbMaxHeight`, `SwimmingClimbMinHeight`, `archSprintCurve`, `fClimbSpeed`, `fDivingAcceleration`, `fDivingDeceleration`, `fDivingMaxSpeed`, `fGravity`, `fJumpHeight`, `fJumpHeightExhausted`, `fSprintingDeceleration`, `fSprintingStrafeLimit`, `fSprintingTurnModifier`, `fSwimmingAcceleration`, `fSwimmingDeceleration`, `fSwimmingMaxSpeed`, `fSwimmingMinDepth`, `fWalkingAcceleration`, `fWalkingDeceleration`, `fWalkingMaxSpeed`, `fWalkingMaxSpeedCrouch`

**`IdleCycleBreaker`** (×1)
- design-time fields also captured as instance state: `fMaxTime`, `fMinTime`

**`PersonalityComponent`** (×1)
- design-time fields also captured as instance state: `Type`

**`enumArmy`** (×1)

**`enumODU`** (×1)

**`ShooterStatus`** (×1)
- design-time fields also captured as instance state: `fCrouchingFactor`, `fDrivingFactor`, `fIronsightFactor`, `fMoveSpeedBabyStepFactor`, `fMoveSpeedJogFactor`, `fMoveSpeedRunFactor`, `fMoveSpeedSprintFactor`, `fMoveSpeedWalkFactor`, `fStandingFactor`, `fSwimmingFactor`, `uiMaxHitPerSecondFactor`

**`TargetStatus`** (×1)
- design-time fields also captured as instance state: `fCrouchingFactor`, `fDrivingFactor`, `fIronsightFactor`, `fMoveSpeedBabyStepFactor`, `fMoveSpeedJogFactor`, `fMoveSpeedRunFactor`, `fMoveSpeedSprintFactor`, `fMoveSpeedWalkFactor`, `fStandingFactor`, `fSwimmingFactor`, `uiMaxHitPerSecondFactor`

**`FOVParameters`** (×1)

**`FOVMultipliers`** (×1)
- design-time fields also captured as instance state: `fCombatMultiplier`, `fNightTimeMultiplier`, `fPlayerInVehicleMultiplier`, `fPostCombatMultiplier`, `fPreCombatMultiplier`, `fSniperAngleMultiplier`, `fSniperLengthMultiplier`

**`DesertFOV`** (×1)

**`SavannahFOV`** (×1)

**`JungleFOV`** (×1)

**`VisibilityEvaluatorParameters`** (×1)

**`Weights`** (×1)
- design-time fields also captured as instance state: `fAmbientLightEvaluatorWeight`, `fDistanceEvaluatorWeight`, `fFOVEvaluatorWeight`, `fOcclusionEvaluatorWeight`, `fPawnSamplingEvaluatorWeight`, `fSpeedEvaluatorWeight`, `fStanceEvaluatorWeight`, `fVegetationEvaluatorWeight`

**`InternalValues`** (×1)
- design-time fields also captured as instance state: `fDistanceEvaluator_FullVisibilityRatio`, `fDistanceEvaluator_MinVisibilityAtMaxFOVRange`, `fFOVEvaluator_VisibilityFactorAtFOVLimit`, `fSpeedEvaluator_StandingStillVisibilityFactor`

**`SocialMechanic`** (×1)
- design-time fields also captured as instance state: `fAimAtDetectionTime`, `fIntrusionDistanceInnerRing`, `fIntrusionDistanceMidRing`, `fIntrusionDistanceOuterRing`, `fMaxChargingAngle`, `fMaxChargingDistance`, `fStareDetectionTime`

**`enumSpecialCharacterType`** (×1)

**`enumAIInfamyMode`** (×1)

**`Collision`** (×1)
- design-time fields also captured as instance state: `fBigCollisionSpeed`, `fMediumCollisionSpeed`, `fSmallCollisionSpeed`, `sndBigCollision`, `sndMediumCollision`, `sndSmallCollision`, `sndmlSoundMultilayerSpeed`, `sndtpSoundType`

**`WaterImpact`** (×1)
- design-time fields also captured as instance state: `sndInWaterImpactSoundID`, `sndOutWaterImpactSoundID`, `sndtpWaterImpactSoundType`

**`MercKitFacialFiles`** (×1)

**`Faces`** (×1)
- design-time fields also captured as instance state: `fileFacialActor`, `sHeadTag`

**`CPickupHealth`** (×1)
- design-time fields also captured as instance state: `bCanBeScouted`, `bPickable`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `bActive`

**`CRealtreeComponent`** (×1)

**`CDelayTriggerComponent`** (×1)
- design-time fields also captured as instance state: `bEnabled`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `IsPaused`, `IsStarted`, `RealDelay`, `TimeElapsed`

**`CPawnPlayerAchievementsComponent`** (×1)

**`CStealthComponent`** (×1)

**`CFCXParticleAmbianceComponent`** (×1)
- design-time fields also captured as instance state: `fExclusionRegionThreshold`

**`CRainComponent`** (×1)
- design-time fields also captured as instance state: `bAutoStart`, `fEmitterDistanceOffset`, `fIntensity`, `fRaysPerSecond`, `fSpeedScaling`, `uiGridSize`, `uiHalfNumGrids`, `uiMaxRaysPerRegion`

**`CZoneInfoComponent`** (×1)
- design-time fields also captured as instance state: `fDensityAdjustmentSpeed`, `fSamplingRadius`, `fWeightDistributionPower`, `fWeightScale`, `uiGridSubdivisions`

**`CPawnMagicCrate`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `AttachVisibleEntity`, `bSwitch`, `fAttachVisibleCounter`

**`CPawnEnemyMonitor`** (×1)

**`CPawnInteractionMonitor`** (×1)

**`CChallengeComponent`** (×1)

**`CEconomyComponent`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `DiamondCount`

**`CFCXCountersComponentPlayerSP`** (×1)
- design-time fields also captured as instance state: `bIsInvincibleExceptToPlayer`
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentAttackType`, `CurrentNbOfAttack`, `DelayBeforeRegen`, `FirePropagationResistantTimer`, `HealWhenExit`, `Invincible`, `RemainingPills`, `SicknessLevel`, `StimImmunity`, `TimeElapsedInAttack`, `TimeElapsedOutsideBubble`, `TimeElapsedSinceLastDamage`, `TimeSinceLastAttack`, `bHealMalariaFirst`, `bIgnoreForceHeal`, `bIsInForcedFailure`, `bNextMinorAttackIsForced`, `bOnDesertZone`, `fBurnDamage`, `fBurnDamageMax`, `fBurnDamageRate`, `fTimeSinceLastTimedSomeoneTalked`, `hidMalariaAnimationLoaded`, `iBaseInfamyLevel`, `iNbOfDaysInThisWorld`, `staminaActionLock`, `staminaFXDrain`, `staminaFXNearZero`, `vNoDesertPos`

**`Stamina`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `CurrentValue`, `LastInstigator`, `MaxValue`, `SendChangedEvents`

**`CHudComponent`** (×1)

**`CPlayerSoundAndFXComponent`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `PGPUnlocked`

**`CCameraShakeAndPadRumbleComponent`** (×1)

**`CVegetationSlowdownComponent`** (×1)

**`CDynLoadComponent`** (×1)

**`CCameraPawnComponent`** (×1)
- savegame-only/computed fields (no `binary_classes.xml` equivalent): `Active`, `FocusEntityID`, `NoiseFOVCurrent`, `NoiseFOVEnabled`, `NoiseFOVTarget`, `NoiseFOVTimeCount`

#### Flat list: all 574 distinct real `entitylibrary.fcb` member names captured somewhere in this save

`BoneId`, `Brain`, `Category`, `ColorIndex`, `Enabled`, `EntityId`, `EntryBoneName`, `ExplosionCenter`, `FirstStateIndex`, `Flags`, `ForceMerge`, `HideBodyYawAngle`, `IsUsableOrientationNeeded`, `LockBone`, `LoggingSize`, `LogicalBind`, `MajorDamageEngineScale`, `MajorDamageEngineStartTime`, `MajorDamageLevel`, `MaxLookAngle`, `MeshIndex`, `MinLookAngle`, `MinorDamageEngineScale`, `MinorDamageEngineStartTime`, `MinorDamageLevel`, `MintEngineStartTime`, `Name`, `PartID`, `Pos`, `RagdollCollideSpeedLimit`, `SwimmingClimbMaxHeight`, `SwimmingClimbMinHeight`, `TextureIndex`, `Type`, `Usable`, `Value`, `WeaponJamProbabilityScale`, `agAmbientGroup`, `aiwsBrainWorkspace`, `archFOVCurveName`, `archGPSVehicleArchetype`, `archGroupNumberCurve`, `archMountedWeapon`, `archSprintCurve`, `archStimEffectTable`, `bAIUserType`, `bAllowAfterCollision`, `bAllowCullBySize`, `bAlwaysShowInReflection`, `bAnimateable`, `bAnimatedControlPos`, `bAutoDraw`, `bAutoReload`, `bAutoStart`, `bBehaveLikeAPickup`, `bBreaksGrass`, `bBurnStim`, `bCanBeScouted`, `bCastAmbientShadow`, `bCastShadow`, `bCheatKnees`, `bCreateAsStatic`, `bCrushStim`, `bDamageable`, `bDisableEnterCollisionDetection`, `bDiscardAfterUse`, `bDisplayOnMap`, `bDriving`, `bEnableHitLocations`, `bEnabled`, `bExplodes`, `bFalloff`, `bFollowEntity`, `bHandBrake`, `bHasALongRangeWeapon`, `bHumanUserType`, `bIgniteOnBurn`, `bIgniteOnCrush`, `bIgniteOnPierce`, `bIgnoreInExplosions`, `bImpulseOnDetach`, `bIntelHackGliderOn`, `bIsAI`, `bIsEnabled`, `bIsInvincibleExceptToPlayer`, `bIsInvincibleToAI`, `bIsInvincibleToPlayer`, `bIsScripted`, `bIsSingleDropImpactObject`, `bKeepAttached`, `bLargeEntity`, `bLastToBeDeleted`, `bLocked`, `bMultiUserType`, `bNeverDelete`, `bOnlyUsableByPlayer`, `bOppositeArmy`, `bOverrideLODSphere`, `bPickable`, `bPierceStim`, `bPlayerOnly`, `bReceiveShadow`, `bShowInReflection`, `bSoundMustFollowEntity`, `bStartEffectsOnCreate`, `bTriggerEffects`, `bUnlimitedAmmo`, `bUpdateRotation`, `bUseExitPointOffset`, `bUseFastCollision`, `bUseMaxTerrainSlope`, `bUseRigidBased`, `bUseTimedSteering`, `disEntityId`, `disFXName`, `disIndex`, `disName`, `distanceInterval`, `driverActionMap`, `entShape`, `eventMask`, `fAccelerationPushFactor`, `fAccelerationsFast`, `fAccelerationsNormal`, `fAccelerationsSlow`, `fAgentHealth`, `fAimAtDetectionTime`, `fAmbientLightEvaluatorWeight`, `fAmplitudeC`, `fAmplitudeVelocityFactor`, `fAngle`, `fBigCollisionSpeed`, `fBrakeLightDimmedFactor`, `fBrakingTorque`, `fBulletImpulseScale`, `fCameraDisplacementFactor`, `fCameraRotationFactor`, `fChassisUnitInertiaPitch`, `fChassisUnitInertiaRoll`, `fChassisUnitInertiaYaw`, `fClimbSpeed`, `fCollisionExtraRadius`, `fCollisionImmunityDelay`, `fCombatMultiplier`, `fCrouchingFactor`, `fDecelerationsFast`, `fDecelerationsNormal`, `fDecelerationsSlow`, `fDensityAdjustmentSpeed`, `fDirtFactor`, `fDisabledResetTime`, `fDistanceEvaluatorWeight`, `fDistanceEvaluator_FullVisibilityRatio`, `fDistanceEvaluator_MinVisibilityAtMaxFOVRange`, `fDivingAcceleration`, `fDivingDeceleration`, `fDivingMaxSpeed`, `fDrivingFactor`, `fDustAvgEmissionDist`, `fDustFactor`, `fDustLifeTimeRatio`, `fDustLifeTimeRatioMax`, `fDustLifeTimeRatioMin`, `fDustRandomDistance`, `fDustSizeRatio`, `fDustSizeRatioMax`, `fDustSizeRatioMin`, `fDynamicLightInnerAngle`, `fDynamicLightOuterAngle`, `fDynamicLightRange`, `fEmitterDistanceOffset`, `fEmptyResetTime`, `fEnginePower`, `fEngineUnderWaterZOffset`, `fExclusionRegionThreshold`, `fExitPointOffset`, `fExplosionDelay`, `fExplosionImpulse`, `fExplosionImpulseScale`, `fExtraClimbEnginePower`, `fExtraTorqueFactor`, `fFOVAngle`, `fFOVEvaluatorWeight`, `fFOVEvaluator_VisibilityFactorAtFOVLimit`, `fFOVTransitionTime`, `fFireDelay`, `fFloatingScale`, `fFootstepsAudibleDistance`, `fFrequency`, `fGearBoxTopSpeed`, `fGravity`, `fGroundFrictionReduceMaxAngle`, `fGroundFrictionReduceMinAngle`, `fHealth`, `fHealthFailureCantDieDuration`, `fHealthFailureLimbsHitModifier`, `fHealthFailureTorsoHitModifier`, `fHeight`, `fHighDirectMaxAngle`, `fHighMaxAngle`, `fHighSteerSpeed`, `fIncomingFireEvasiveness`, `fInitialReliability`, `fIntensity`, `fIntrusionDistanceInnerRing`, `fIntrusionDistanceMidRing`, `fIntrusionDistanceOuterRing`, `fIronsightFactor`, `fJumpHeight`, `fJumpHeightExhausted`, `fJumpOutBrakeFactor`, `fJumpOutMinSpeed`, `fKickForce`, `fKillDistance`, `fLODSphereRadius`, `fLeanFactor`, `fLength`, `fLowDirectMaxAngle`, `fLowMaxAngle`, `fLowSteerSpeed`, `fMarkerZ`, `fMass`, `fMaxChargingAngle`, `fMaxChargingDistance`, `fMaxFallingDist`, `fMaxRPM`, `fMaxSlope`, `fMaxSpeed`, `fMaxTerrainSlope`, `fMaxTime`, `fMediumCollisionSpeed`, `fMinCollisionSpeed`, `fMinFallingDist`, `fMinRPM`, `fMinSpeed`, `fMinTime`, `fMinimalCollisionSpeed`, `fMissHeight`, `fMissWidth`, `fMoveSpeedBabyStepFactor`, `fMoveSpeedJogFactor`, `fMoveSpeedRunFactor`, `fMoveSpeedSprintFactor`, `fMoveSpeedWalkFactor`, `fNightTimeMultiplier`, `fOcclusionEvaluatorWeight`, `fPartsSpeed`, `fPawnSamplingEvaluatorWeight`, `fPawnsLookingRadius`, `fPawnsTooCloseRadius`, `fPhysImpulse`, `fPlayerInVehicleMultiplier`, `fPointBlankDistance`, `fPostCombatMultiplier`, `fPreCombatMultiplier`, `fRPMMaxAngle`, `fRadius`, `fRaysPerSecond`, `fRearWheelHandBrakeFrictionScale`, `fRunOverSoundRange`, `fSamplingRadius`, `fSeatEntryMaxRadius`, `fSelfCollOverrideSpeed`, `fSmallCollisionSpeed`, `fSniperAngleMultiplier`, `fSniperLengthMultiplier`, `fSpeed`, `fSpeed50kmhAngle`, `fSpeedCutOff`, `fSpeedEvaluatorWeight`, `fSpeedEvaluator_StandingStillVisibilityFactor`, `fSpeedForMaxDropImpactVolume`, `fSpeedHigherBound`, `fSpeedLowRangeAngle`, `fSpeedLowRangeSpeed`, `fSpeedRelevance`, `fSpeedScaling`, `fSpeedToPlayFallingSound`, `fSpeedsBabyStep`, `fSpeedsJog`, `fSpeedsRun`, `fSpeedsSprint`, `fSpeedsWalk`, `fSprintingDeceleration`, `fSprintingStrafeLimit`, `fSprintingTurnModifier`, `fStanceEvaluatorWeight`, `fStandCapsuleRadius`, `fStandingFactor`, `fStareDetectionTime`, `fStrength`, `fSuspDampingCompression`, `fSuspDampingRelaxation`, `fSuspLength`, `fSuspStrength`, `fSuspensionHeavySpeed`, `fSuspensionMediumSpeed`, `fSuspensionSmallSpeed`, `fSwimmingAcceleration`, `fSwimmingDeceleration`, `fSwimmingFactor`, `fSwimmingMaxSpeed`, `fSwimmingMinDepth`, `fThrustPedalStopFadeOut`, `fThrustPedalStopThreshold`, `fTimerToMissTarget`, `fTimerToPointBlank`, `fTorquePitchFactor`, `fTorqueRollFactor`, `fTorqueYawFactor`, `fUnderWaterMaxDepth`, `fVariationBabyStep`, `fVariationJog`, `fVariationRun`, `fVariationSprint`, `fVariationWalk`, `fVegetationEvaluatorWeight`, `fWalkingAcceleration`, `fWalkingDeceleration`, `fWalkingMaxSpeed`, `fWalkingMaxSpeedCrouch`, `fWaterFriction`, `fWeightDistributionPower`, `fWeightScale`, `fWheelOffsetZ`, `fWheelsEnterWaterFadeoutTime`, `fWidth`, `fWindFactor`, `fZSpeedForMaxSplashVolume`, `fZSpeedToTriggerSplash`, `fileBoxPath`, `fileFacialActor`, `fileFacialFile`, `fileName`, `filePawnStateMachine`, `fileRagdoll`, `fileSkeleton`, `hidAngles`, `hidBoneIndex`, `hidCategory`, `hidComponentClassName`, `hidConstEntity`, `hidDescriptor`, `hidDetailObject`, `hidEntityClass`, `hidEventName`, `hidFXNode`, `hidGraphicIndex`, `hidGroundColor`, `hidHasAliasName`, `hidHasAmbientValues`, `hidHasStatic`, `hidHeightAbove`, `hidHighresRigidbodyName`, `hidIndex`, `hidIsFrame`, `hidMeshName`, `hidMissionLayerPath`, `hidName`, `hidNewCollision`, `hidNodeName`, `hidNodeNameLOD0`, `hidNodeType`, `hidObjectHeight`, `hidPartId`, `hidPos`, `hidPos_precise`, `hidResIndex`, `hidResourceCount`, `hidResourceId`, `hidResourceIndex`, `hidRigidbodyIndex`, `hidRigidbodyName`, `hidShapeType`, `hidShowRadius`, `hidShowType`, `hidSkyOcclusion0`, `hidSkyOcclusion1`, `hidSkyOcclusion2`, `hidSkyOcclusion3`, `hidStartOnLoad`, `hidTargetEntityId`, `hidTypeNameIndex`, `iAnimVehicleType`, `iAnimationValue`, `iBailoutCrushStimLevel`, `iMaxAmmo`, `iMercDensity`, `iMercDensityThreshold`, `iMinAmmo`, `iRepairSteps`, `index`, `logmatFakeMulletMaterial`, `m_AlertClearVal`, `m_AlertFuzzyVal`, `m_CombatClearVal`, `m_CombatFuzzyVal`, `m_DeadClearVal`, `m_DeadFuzzyVal`, `m_IdleClearVal`, `m_IdleFuzzyVal`, `m_SocialClearVal`, `m_SocialFuzzyVal`, `m_SpecialClearVal`, `m_SpecialFuzzyVal`, `m_ThresholdClearVal`, `m_ThresholdFuzzyVal`, `m_VehicleClearVal`, `m_VehicleFuzzyVal`, `matimpBigCollisionImpact`, `matimpDropImpact`, `matimpFakeBulletFx`, `matimpFootStepThird`, `matimpLanding`, `matimpMediumCollisionImpact`, `matimpSmallCollisionImpact`, `matimpWheelDustFx`, `mixInVehicleSoundPreset`, `nFalloffMinLevel`, `nGears`, `nInstantExplosionCrushMaxHealth`, `nInstantExplosionCrushThreshold`, `nLevel`, `nMaxFallingCrushLevel`, `nMaxRandomColorIndex`, `nMaxStimCollisionLevel`, `nMinFallingCrushLevel`, `nMinRandomColorIndex`, `nStartStateIndex`, `nSurfaceIdx`, `nVehicleColor`, `objModel`, `olgLightGroup`, `packInventoryPack`, `psBrokenEngineSmoke`, `psBrokenEngineSmokeNoHood`, `psDiveMove`, `psEmerge`, `psEmitter`, `psEngineExplosion`, `psEngineFire`, `psEngineFireNoHood`, `psEngineSmoke`, `psEngineSmokeNoHood`, `psFxExhaust`, `psFxWaterSplash`, `psStorm`, `psSubmerge`, `psSwimIdleChest`, `psSwimIdleHands`, `psSwimMoveChest`, `psSwimMoveHands`, `sDetail`, `sEnterSignal`, `sEnterUsageString`, `sHeadTag`, `sInitialWeaponCategory`, `sKickUsageString`, `sLeaveTransitionSignal`, `sName`, `sPartName`, `sRepairUsageString`, `sSeatBoneName`, `selAIInfamyMode`, `selArmy`, `selIntelType`, `selLevel`, `selNextState`, `selODU`, `selPrimitiveType`, `selSpecialCharacterType`, `selState`, `selType`, `selVehicleColor`, `selVehicleType`, `sndBigCollision`, `sndBrake`, `sndEngineBurning`, `sndEngineExplosion`, `sndEngineIgnition`, `sndEngineLoop`, `sndEngineMajorDamage`, `sndEngineMinorDamage`, `sndExitGroupSound`, `sndExitSound`, `sndExtraTorqueEngineLoop`, `sndFallingSound`, `sndFrameLoop`, `sndGearShift_MajorDamage`, `sndGearShift_MinorDamage`, `sndGearShift_New`, `sndId`, `sndInWaterImpactSoundID`, `sndInitSound`, `sndKickSoundID`, `sndLandingFatalSoundID`, `sndMediumCollision`, `sndOutWaterImpactSoundID`, `sndPlayEngineIdleLoop`, `sndRollingSound`, `sndRollingSoundEnd`, `sndSlidingSound`, `sndSlidingSoundEnd`, `sndSmallCollision`, `sndStopEngineIdleLoop`, `sndSuspensionSoundHeavy`, `sndSuspensionSoundMedium`, `sndSuspensionSoundSmall`, `sndThrustPedal`, `sndTurnOffEngine`, `sndWheelRoll_1`, `sndWheelRoll_2`, `sndWheelRoll_3`, `sndWheelRoll_4`, `sndWheels_EnterWater`, `sndWheels_RunOver`, `sndmlDamageSoundMultilayer`, `sndmlExtraTorqueSoundMultilayer`, `sndmlObjectMovementMultilayer`, `sndmlRPMSoundMultilayer`, `sndmlSoundMultilayerSpeed`, `sndmlSpeedSoundMultilayer`, `sndmlThrustPedalSoundMultilayer`, `sndmlWheelSlipSoundMultilayer`, `sndptSoundPoint`, `sndswtpFootstepSpeedSwitchType`, `sndswtpMaterialSoundSwitchType`, `sndswtpReliabilitySoundSwitchType`, `sndswvlBrokenSoundSwitchValue`, `sndswvlFootstepSwitchValue`, `sndswvlMajorDamageSoundSwitchValue`, `sndswvlMinorDamageSoundSwitchValue`, `sndswvlNoDamageSoundSwitchValue`, `sndtpFallingType`, `sndtpKickSoundType`, `sndtpLandingFatalSoundType`, `sndtpRollingSoundType`, `sndtpSlidingSoundType`, `sndtpSoundType`, `sndtpWaterImpactSoundType`, `stimIgniteId`, `stimIgniteIdMP`, `texTexture`, `timeInterval`, `tplCreatureType`, `uiGridSize`, `uiGridSubdivisions`, `uiHalfNumGrids`, `uiMaxHitPerSecondFactor`, `uiMaxRaysPerRegion`, `useDistanceInterval`, `vColor`, `vInitialPos`, `vPos`, `vPosition`, `vecSelfVelocity`, `vecStandCapsulePointA`, `vecStandCapsulePointB`, `vectorCenterOfMassOffset`, `vectorDlcSwitchAnglesOffset`, `vectorDlcSwitchPosOffset`, `vectorEngineOffset`, `vectorFxWaterSplashOffset`, `vectorNeutral`, `vectorQ0`, `vectorQ1`, `vectorQ2`, `vectorQ3`, `vectorQ4`, `vectorQ5`, `vectorQ6`, `vectorQ7`, `vectorSize`, `vehicleMaxLookAngle`

#### Top-level sections directly under the root `CampaignSave` object

- `Entity` (×102)
- `WaterLevels`
- `Sounds`
- `CommandMap`
- `Listeners`
- `PlayingSequences`
- `TimeOfDay`
- `RainEvaluator`
- `MovieSequencesToUnload`
- `MissionManagement`
- `JackalTapes`
- `PartnerTapes`
- `ActiveMissions`
- `LuaGlobals`
- `BuddyManagement`
- `MainHud`
- `WeaponKills`
- `PlayerStats`
- `BuddyRescue`
- `BuddyDown`
- `GameplayManagement`
- `MissionManager`
- `GhostGroups`
- `GhostDB`
- `AIObjectID`
- `CollectiveBlackboard`
- `BlueArmy`
- `RedArmy`
- `GreyArmy`
- `NeutralArmy`
- `WagerRegions`
- `Implementation`
- `AbsolutePresets`
- `RelativePresets`
- `ObjectiveStatesToBlock`
- `SystemGroups`
- `PersistenceDB`
- `ResourceEntries`

### Caveats

- **This is one save's snapshot, not an exhaustive ceiling.** Which entities have a `PersistenceDB`
  record — and therefore which classes/fields appear in a measurement like this at all — depends on
  what that specific playthrough touched. A longer save, or a different route through the campaign,
  would plausibly freeze a larger and slightly different subset. Treat 237 classes / 574 members as "at
  minimum this much," not "the complete set of everything that can ever be captured."
- A name appearing in a class's "design-time fields also captured" list means *this save's copy* of
  that entity is frozen for that field — not that every entity of that class in every save is frozen.
  An entity nobody has interacted with is unaffected regardless of class.
- A hash match between a save's captured field and a `binary_classes.xml` member name is strong
  evidence, not proof — CRC32 collision between two unrelated values is vanishingly unlikely at this
  sample size, but not formally impossible.
- Savegame-only fields aren't necessarily irrelevant to modding — most (e.g. `AIObject`'s AI-state
  fields) are genuinely dynamic runtime state with no design-time equivalent, but a few (e.g.
  `Health`/`Stamina`'s `MaxValue`) are plausibly an internally differently-named mirror of a real
  design field that this pass's literal string-equality check couldn't match.

## Unknowns (entity-library overlap)

- Attributing a captured field block to a *specific* entity archetype, not just the shared component
  class that captured it — e.g. which playable/NPC archetypes actually reference the `CGameAgent`/
  `Body` AI-tuning cluster.
- Whether savegame-only fields with a plausible design-time cousin (`MaxValue` on `Health`/`Stamina`,
  `CurrentHealth` on several phys components) are an aliased/renamed mirror of a real
  `entitylibrary.fcb` field, or genuinely archetype-less runtime state — would need each component's
  `RegisterProperties` compared directly against its `entitylibrary.fcb` declaration.
- How much the 237/574 figures move across the other ~30 save files in the same folder, and whether
  any classes/fields are load-bearing across every playthrough versus only longer/more-completionist
  ones.

## Reproducing this

Export a save's `PersistenceDB` tree via JackAll's Saves tab (or `JackAll.Cli`'s save-reading path),
then cross-reference every `<object type="X">`/`<value name="Y">` pair against
`tools/JackAll/assets/binary_classes.xml`'s own `<class name="X">`/`<member name="Y">` declarations —
pure string equality, no hash-matching or Ghidra access needed for this part.
