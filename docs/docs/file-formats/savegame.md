---
sidebar_position: 6
---

# `.sav` — Savegame Container Format

:::info[Verified via reverse engineering]
See [the engine overview](../engine-internals/overview.md) for binary identification. Goal: reverse
the on-disk format of `Documents\My Games\Far Cry 2\Saved Games\<slot>.sav`, which no prior
community tooling had documented at the byte level — see "What research already covered" below.
:::

## Status: structure confirmed byte-for-byte against a real save file

Unlike the other files in this note set, this investigation worked **bidirectionally**: a real save
(`178430170947.sav`, 1,854,505 bytes, hex-dumped directly from the user's own
`Documents\My Games\Far Cry 2\Saved Games\`) was inspected first, byte offsets were hypothesized,
and every hypothesis below was then confirmed by decompiling the actual writer/reader functions and
checking their arithmetic against the same file's bytes. Every offset in the tables below is a
**measured fact from a real save**, not just a decompiled guess — where the two disagree, that's
called out explicitly.

## IMPORTANT: this required a second binary — `FarCry2_server`, not `Dunia.dll`

All prior `reverse/dunia/*.md` files were traced against the Windows PC `Dunia.dll` (Steam v1.03,
MSVC-mangled symbols, load base `0x10xxxxxx`, see [the engine overview](../engine-internals/overview.md)). That binary's
`.sav`/`CGameFile*`/`CScreenShot`/`CPersistenceDB` code is present but was never named/demangled
(these are internal statics, no export table entry, and `Dunia.dll`'s own PDB/RTTI-derived names for
them hadn't been recovered as of this session).

The Ghidra project (`reverse/fc2.gpr`) turned out to already contain a **third program**, not
previously documented in this note set: `reverse/fc2.rep/idata/00/00000002.prp` names it
`FarCry2_server` — the **Linux dedicated-server binary** (confirmed via `list_segments`: ELF with
`.dynamic`/`.got.plt`, ~`0x08048000` load base; confirmed via `list_imports`: POSIX/glibc imports
like `pthread_create`, `mkdir`, `gethostbyname`, `listen` — nothing Windows-specific). Its symbols are
**GCC/Itanium-mangled** (`_ZN14CPersistenceDB...`), not MSVC-mangled, and critically **this build is
largely unstripped** (`.symtab`/`.strtab` present with full C++ class/method names), unlike the PC
`Dunia.dll`. Since the dedicated server links the same shared Dunia engine source as the PC client
(persistence, save/load, screenshot-thumbnail and game-file-list code is all present and appears
fully linked even though a headless server never actually writes a player `.sav`), this binary is a
**far better source for real class/method names for this subsystem** than the PC exe, even though
byte-exact offsets were independently re-verified against the real Windows-written save file below.

**Practical implication for future sessions**: addresses in the `0x08xxxxxx`/`0x09xxxxxx`/`0x0axxxxxx`
range in this Ghidra project belong to `FarCry2_server`, not `Dunia.dll` — do not assume a bare hex
address implies the PC engine DLL the way every other file in this note set does. This distinction
should be treated as a standing correction to this whole note set's "which binary is this address in"
assumption; [the engine overview](../engine-internals/overview.md) should be consulted alongside this file if that matters for
future work here.

## What research already covered (before this session)

`research/` had **no dedicated savegame-format research** — grepping `research/knowledge.md`,
`mods_survey.md`, and `modding_guide.md` for `save`/`.sav`/`PersistenceDB` turned up only save-game
*editing folklore*, not format documentation:

- Save folder location: `Documents\My Games\Far Cry 2\` (community-sourced; independently confirmed
  at the binary level in [the save-data path notes](../engine-internals/save-data-path.md), which traced the `SHGetFolderPathA`
  + `"My Games\Far Cry 2\"` string concatenation but never located the actual `.sav` writer).
- [the command-line args notes](../engine-internals/command-line-args.md) already noted `-load <savename>` "walks
  `PersistenceDB`, reads back a `PlayerPos` property afterward" — a prediction from `ParseLoadSaveArgs`
  disassembly alone, now corroborated: `PersistenceDB` really is the class whose dump makes up the
  bulk of the file (see below), though `PlayerPos` itself wasn't located as a literal string this
  session (see "Not yet traced").
- `mods_survey.md` §4 notes **"some values are cached in savegames"** (jump height, outpost-clear
  timers) and that reputation/infamy numbers alone don't reproduce organically-earned NPC behavior —
  consistent with this session's finding that the bulk of the file is a generic entity/property
  dump (below), not a small fixed set of named gameplay stats: there's no reason to expect any one
  cached value to behave specially, they're all just properties on entities in the same tree.
- No community tool (Gibbed's, JackAll, or anything referenced in `mods_survey.md`) was ever found to
  parse `.sav` itself — only the `.fcb`-format *data* files ship with editors. This session's biggest
  finding — that `.sav` embeds a plain `.fcb` blob using the exact format already reverse-engineered
  in [the `.fcb` format page](./fcb.md) — means **JackAll's existing `FcbDocument` reader/writer should
  work on the embedded blob directly**, once it's sliced out at the right offset (see below). This was
  never attempted or mentioned anywhere in `research/`.

## Confirmed container structure

The file is a flat, uncompressed, unencrypted concatenation of four sections, each independently
serialized by its own C++ class in the shared engine code. Class names and the exact
`GetSaveSize()`/`WriteToFile()`/`ReadFromFile()` logic below are decompiled from `FarCry2_server`
(GCC-mangled demangled names shown as Ghidra resolved them); byte offsets are measured directly from
`178430170947.sav`.

```
[0]      CGameFileHeader base fields          20 bytes    (fixed size, GetSaveSize() == 0x14 literal)
[0x14]   + CCampaignGameFileHeader extension   var         (2 length-prefixed strings + 12 fixed bytes)
[0x39]   CScreenShot (thumbnail)               var         (16-byte header + WxHxChannels pixel blob + metadata)
[0xB44D] CCampaignGameFileData                 var         (DLC-id string list, then...)
[...]      → an embedded, ordinary .fcb blob   var         (PersistenceDB entity/state dump — see below)
```

Confirmed by direct size arithmetic against the real file (all four numbers below were independently
computed from decompiled `GetSaveSize()` bodies, then checked against the measured file):

| Section | Formula | This save's value | Measured file offset |
|---|---|---|---|
| `CGameFileHeader` base | constant `0x14` | 20 | `0x00`–`0x13` |
| `CCampaignGameFileHeader` extra | `0xC` fixed `+ GetStringSaveSize(world) + GetStringSaveSize(player)` | 12+10+15 = 37 | `0x14`–`0x38` |
| `CScreenShot` | `16 (header) + W·H·channels·bitsPerChannel/8 + 4 (metadata count) + Σmetadata` | 16+46080+4+0 = 46100 | `0x39`–`0xB44C` |
| `CCampaignGameFileData` DLC list + reserved | `4 (count) + Σ(4+strlen) per DLC` `+ 4` (extra field, see `GetSaveSize`) | 12+4 = 16 | `0xB44D`–`0xB45C` |
| embedded `.fcb` blob | `Fcb_ReadHeader`-compatible, size = virtual `GetSaveSize()` on the persistence object | 3,753,895+ (rest of file) | `0xB45D`–EOF |

Every section boundary above lines up exactly with the arithmetic — there is no padding/alignment
between sections anywhere in the file.

## Section 1 — `CGameFileHeader` base (20 bytes, offset `0x00`)

`CGameFileHeader::GetSaveSize()` (`FarCry2_server @ 0x091e3810`) is a **hardcoded return of `0x14`**
— this base header is always exactly 20 bytes regardless of content. The constructor
(`CGameFileHeader::CGameFileHeader`, `@ 0x091e3820`) only initializes 8 of those bytes generically
(a vtable-adjacent field and a 4-byte "ID" defaulted from a global `InvalidID` sentinel); the actual
`WriteToFile` for this base class wasn't isolated this session (see "Not yet traced"), so field
semantics below are **inferred from the real bytes**, not decompiled:

```
offset  size  field                     measured value (this save)      confidence
0x00    4     u32, "ID"/tag field        0x0000000A (10)                 low — not a plausible Unix
                                                                          timestamp (decodes to
                                                                          2022-12-30, doesn't match
                                                                          the file's actual save
                                                                          date); likely a small
                                                                          save-type/slot enum or a
                                                                          populated CStringID-style
                                                                          hash remnant of the ctor's
                                                                          default "InvalidID" field
0x04    4     float, likely player X     ≈2621.3                        medium
0x08    4     float, likely player Y     ≈2109.1                        medium
0x0C    4     float, likely player Z     ≈17.9                          medium — small Z plausible
                                                                          as elevation on world1
```

The 3-float reading is circumstantial but self-consistent: `command_line_args.md` already predicted
a `PlayerPos`-shaped property gets read back after `-load`, and X/Y magnitudes in the low thousands
with a small Z are the right order of magnitude for `world1`'s map extents (vs. e.g. a timestamp,
GUID, or CRC, none of which predicts 3 well-formed non-degenerate IEEE-754 floats in a row). Treat
this as a strong hypothesis, not a confirmed field mapping — the actual `WriteToFile` populating
these 16 bytes wasn't located (this class's `WriteToFile`/`ReadFromFile` weren't found under that
literal name in the symbol table the way `CScreenShot`'s were — see "Not yet traced").

## Section 2 — `CCampaignGameFileHeader` extension (offset `0x14`)

`CCampaignGameFileHeader::GetSaveSize()` (`@ 0x0896ef10`):
```c
GetSaveSize() = CGameFileHeader::GetSaveSize() + 0xc
              + GameFileUtils::GetStringSaveSize(this+0x18)   // world name
              + GameFileUtils::GetStringSaveSize(this+0x20);  // player/character name
```
`GetStringSaveSize` costs `4 + strlen` (a `u32` length prefix, **no null terminator** — this is a
different string encoding than the null-terminated strings found later inside the embedded `.fcb`
blob, see below). Measured in the real file, in this exact byte order:

```
offset  size        field                    measured value
0x14    4+6=10      len-prefixed string       "world1"
0x1E    4+11=15     len-prefixed string       "Paul_Ferenc"     (player/character name)
0x2D    4           u32                       1
0x31    4           u32                       8
0x35    4           u32                       2
```

The trailing three `u32`s are the `0xc` (12-byte) fixed extra this class's `GetSaveSize` adds on top
of the two strings. No accessor named `GetDifficulty`/`GetAct`/`GetChapter` was found on this class
in `FarCry2_server`'s symbol table to pin these down definitively (a `Difficulty`-named
symbol/accessor cluster does exist engine-wide — `GetDifficultyLevel`, `SetDifficultyLevel`,
`GetDifficultyFactor` — but none is provably wired to this specific class from what was traced this
session). Plausible reading given values `(1, 8, 2)` observed: difficulty tier, current
act/chapter-like progress marker, and a third small enum — **not confirmed**, flagged for follow-up.

## Section 3 — `CScreenShot` (thumbnail image, offset `0x39`)

Fully confirmed both directions — `CScreenShot::WriteToFile` (`@ 0x091eaa00`) and
`CScreenShot::ReadFromFile` (`@ 0x091ebae0`) are exact mirror images of each other, and both match the
real file byte-for-byte:

```
offset       size         field
0x39         4            width   (u32) — 128 in this save
0x3D         4            height  (u32) — 90
0x41         4            "channels" (u32) — 4  (RGBA/BGRA component count)
0x45         4            "bits-per-channel-ish" (u32) — 8
0x49         W·H·ch·bpc/8 raw pixel bytes — 128·90·4·8/8 = 46,080 bytes
0xB449       4            metadata-entry count (u32) — 0 in this save
0xB44D       —            (0 metadata entries here; see ScreenShot::WriteMetaDataInfoToFile /
                            ReadMetaDataInfoFromFile for the per-entry format when count > 0 —
                            not traced this session, no real sample had any)
```

Pixel format wasn't independently confirmed as BGRA vs RGBA (no distinguishing feature found in this
sample — a spot check of raw bytes showed low, similar-magnitude values across all 3 color channels,
consistent with a dark/foliage-heavy in-game screenshot either way). Byte order aside, the **size
formula is confirmed exactly**: `width * height * channels * bitsPerChannel / 8` — with
`channels=4, bitsPerChannel=8` this simplifies to plain `width*height*4`, matching the measured
46,080-byte blob length exactly (verified by locating the next section's known-good magic constant
immediately afterward, see below).

`CGameFilesService::GrabScreenshotEv` / `CScreenShot::Capture(unsigned short, unsigned short)` are the
capture-side entry points (not decompiled this session); `SetGameFileThumbnail` /
`SetGameFileThumbnailPS3` on `CGameFileInfo` are the two platform-specific attach points feeding into
whichever concrete `CScreenShot` gets embedded — the `PS3`-suffixed one is dead code in this
particular (Linux server) binary, evidence the same source file compiles for multiple platforms
behind runtime rather than `#ifdef` branching in at least this class.

## Section 4 — `CCampaignGameFileData` (offset `0xB44D`): DLC list + embedded `.fcb` blob

`CCampaignGameFileData::GetSaveSize()` (`@ 0x0896ee90`):
```c
GetSaveSize() = 4                                        // DLC-string count
              + Σ GameFileUtils::GetStringSaveSize(dlc)   // one entry per active DLC id
              + 4                                         // extra fixed field (unidentified)
              + persistenceObj->GetSaveSize();            // virtual call, this+4 — the FCB blob
```

Measured:
```
offset       size    field
0xB44D       4       DLC count (u32) — 1
0xB451       4+4=8   len-prefixed string "dlc1"
0xB459       4       u32 — 0   (the "+4 extra fixed field" from GetSaveSize; purpose unconfirmed)
0xB45D       —       embedded .fcb blob starts here (see below) — runs to end of file
```

### The embedded blob is a plain, ordinary `.fcb` file — confirmed against [the `.fcb` format page](./fcb.md)

Bytes at `0xB45D` decode exactly per the already-documented `.fcb` header
([the `.fcb` format page](./fcb.md)), with **no wrapper, no extra length prefix, no compression** — it's
byte-identical to what `Fcb_ReadHeader` expects from a standalone `.fcb` file on disk:

```
0xB45D   4    magic (u32)      0x4643626E  "FCbn"   — == Fcb_MagicConstant(), confirmed
0xB461   2    version (u16)    2                     — == Fcb_SupportedVersionConstant(), confirmed
0xB463   2    flags (u16)      0                      — string-hashed-TypeHash path not used here either
0xB465   4    totalObjectCount (u32)   73,200 (0x11DF0)
0xB469   4    totalValueCount  (u32)   73,199 (0x11DEF)
0xB46D   —    root object tree starts here (Fcb_ParseObject rules apply verbatim)
```

Manually walked the root object per `Fcb_ParseObject`'s documented rules and it decodes perfectly:

```
0xB46D  1 byte    childCount = 0x8B = 139           (< 0xFE, literal)
0xB46E  4 bytes   TypeHash = 0x9C44B3A3
0xB472  1 byte    valueCount = 0x65 = 101            (< 0xFE, literal)
0xB473  4 bytes   value[0].nameHash = 0x9F60B705
0xB477  1 byte    value[0] size-varint = 0x0E = 14   (< 0xFE, literal — 14 payload bytes follow)
0xB478  14 bytes  value[0] payload = "Addi Mbantuwe\0"
```

`"Addi Mbantuwe"` is a Far Cry 2 buddy/companion NPC name — this is a `CBindingHierarchyDBRec`/
`CPersistenceDBRec` entry for a persisted world entity, exactly matching what
`CPersistenceDB::SaveDB` (`@ 0x0967e350`, decompiled this session) actually writes: it walks the DB's
binding-hierarchy tree and, per entity, serializes through `CNomadObjectDescriptor::SaveState` using a
set of named tags recovered directly from the decompiled body —
`Tag_HierarchiesQueue`, `Tag_EntityId`, `Tag_Hierarchy`, `Tag_Entities`, `Tag_HierarchyRecord`,
`Tag_Id`, `Tag_Record`, `Tag_State`, `Tag_Description`, `Tag_HierarchyId`, `Tag_OmniEntities`. These
tags are almost certainly the human-readable field names whose `GetNameHash`/`CRC32_Hash` values are
the `nameHash`s stored in each `.fcb` value entry (the same mechanism `fcb_format.md` documents for
ordinary data files) — i.e. **the savegame's bulk content is the entire `PersistenceDB` — every
spawned/moved/killed entity's state in the game world, from buddies down to individual dropped
items — serialized through the exact same generic `.fcb` writer used for the game's shipped data
files**, not a bespoke savegame-specific format. This directly explains the `mods_survey.md` §4 note
that jump-height/outpost-timer changes "are cached in savegames" — they're just more properties on
more entities in this same generic tree, no different in kind from anything else persisted here.

**Practical consequence for tooling**: JackAll's existing `FcbDocument` reader
(`tools/JackAll/src/JackAll.Core/Format/Fcb/FcbDocument.cs`), already validated against 5 shipped
`.fcb` fixtures per `fcb_format.md`, should be able to parse this blob directly once sliced out at
its confirmed start offset (immediately after the DLC-list section, formula above) — no new binary
format work should be needed to read (or, cautiously, write back) a save's entity tree, only the
four wrapper sections documented above.

## Confirmed: the persistence tag vocabulary, and `binary_classes.xml` actually DOES resolve a real chunk of this content

Follow-up investigation (separate session) prompted by a very reasonable question: JackAll's existing
`.fcb` viewer resolves type/value name hashes via `tools/JackAll/assets/binary_classes.xml`
(`FcbClassDefinitions`, see [the `.fcb` format page](./fcb.md)) — how much of a real save's exported
`.fcb` tree does that actually resolve?

**Correction to this file's own earlier claim in this same investigation**: an early pass at this
question measured "226,187 `hash="..."` occurrences, 0 resolved" and concluded `binary_classes.xml`
resolves nothing at all. That count was real but the conclusion drawn from it wasn't checked properly
— it never actually verified `name="..."`/`type="..."` occurrences separately from the coincidental
`type="BinHex"` attribute that appears on every unresolved value regardless. Measured properly this
session: **42,290 of 114,553 `<object>` tags (37%) already resolve to a real class name, and 24,382 of
178,306 `<value>` tags (14%) already resolve to a real member name** — both via the *existing*,
unmodified per-class-scoped resolution, nothing added this session. The reason: a save's persisted
entity records embed the entity's own live component tree as nested sub-objects (`CIgnitorComponent`,
`CCompoundPhysComponent`, `CPersistComponent`, `RootNode`, ...) — genuinely part of
`entitylibrary.fcb`'s own class vocabulary, reused verbatim because it's the same live C++ objects
being serialized, just reached through the persistence system rather than a level's own entity
placement data. Only the *outer* wrapper layer (`CPersistenceDBRec`/`CBindingHierarchyDBRec` and their
own structural tags) is the genuinely disjoint, uncatalogued vocabulary this section is mostly about.

`binary_classes.xml` is a community catalog of `entitylibrary.fcb`-style **data-file** classes —
built by people who could only ever see strings that appear in a shipped `.fcb` file. The *wrapper*
layer of a save's `PersistenceDB` dump uses a disjoint set of names that live only inside the game's
own compiled code (`CNomadObjectDescriptor::RegisterProperties` calls, and the handful of structural
`Tag_*` globals `CPersistenceDB::SaveDB` uses — see below), never inside any shipped data file, so
nobody could have captured *those specific* names the way `binary_classes.xml` was built. Both systems
share the same low-level plumbing (the generic `.fcb`/`FCbn` container format, and the same
`CRC32_Hash` name-hashing function everywhere in the engine — confirmed in [the engine overview](../engine-internals/overview.md))
but the wrapper layer hashes a different **vocabulary** of strings than `binary_classes.xml` catalogs.

**The `Tag_*` global variable names turn out to be the literal runtime string content, confirmed
against real data.** `CPersistenceDB::SaveDB`'s decompile only gave symbol names like `Tag_Id` — a
compiler debug-info label, not proof of the actual string a `CStringID`/tag object was constructed
with. Tested it directly: CRC32-hashed each candidate name the same way the engine does
(`FcbClassDefinitions.Crc32Ascii`, already validated elsewhere) and grepped for the result across a
real save's exported `.fcb` XML. Six matched, thousands of times each — far too consistent to be
coincidence:

| Tag | CRC32 | Occurrences in one real save's tree |
|---|---|---|
| `"Id"` | `0x2ABD43F2` | 4,870 |
| `"State"` | `0x6252FDFF` | 3,545 |
| `"EntityId"` | `0x0F5E4BAA` | 2,773 |
| `"HierarchyId"` | `0xA9100FC2` | 2,334 |
| `"Record"` | `0x9C989AA7` | 2,651 |
| `"HierarchyRecord"` | `0x7A2B069C` | 2,154 |
| `"Description"` | `0xEB78CFF1` | 485 (matches `SaveDB`'s conditional `GetChildDescription` branch — not written for every record, unlike the others) |
| `"Entities"` | `0xA99A06B3` | 2 |
| `"Hierarchy"` | `0x788BAA0D` | 2 |
| `"HierarchiesQueue"` | `0x7C1C0FBA` | 2 |
| `"OmniEntities"` | `0x5134EF37` | 2 |

The last four sit at 2 occurrences each because they're top-level container/wrapper tags (used once
or twice near the root), not per-record fields — consistent with `SaveDB`'s two-phase walk (a
hierarchy-based section, then a second pass for entities with no `BindingHierarchy` at all, tagged
`OmniEntities`).

**Went one step further and found actual registered member names, not just navigation tags.** Both
`ms_descriptor` pointers `SaveDB` references (`PTR_ms_descriptor_0a4023d4` for
`CBindingHierarchyDBRec`, `PTR_ms_descriptor_0a41326c` for `CPersistenceDBRec`) are read (not just
referenced) from inside a function literally named `RegisterProperties` — traced via `get_xrefs_to`
on those two addresses directly, sidestepping the ~100 identically-named `RegisterProperties`
functions engine-wide entirely (no brute-force search needed once the descriptor addresses were
already in hand from the earlier `SaveDB` decompile). Their bodies (`FarCry2_server @ 0x09679a93` /
`@ 0x09679b22`) are plain, un-obfuscated: each calls `CNomadObjectDescriptor::PushBackMember` once per
registered field, with the field's real name as a literal C string argument.
`CBindingHierarchyDBRec::RegisterProperties` registers three: `"MemoryUsage"`, `"PersistType"`,
`"BindingHierarchy"`. Same CRC32-and-grep verification as above:

| Field | CRC32 | Occurrences | Confirmed? |
|---|---|---|---|
| `"MemoryUsage"` | `0x65A0E5B6` | 4,488 | yes |
| `"PersistType"` | `0x4A1FC981` | 2,154 | yes — exactly matches `HierarchyRecord`'s own count above, i.e. one `PersistType` per hierarchy record, as expected for a sibling field on the same object |
| `"BindingHierarchy"` | `0xE2C5EA2C` | 0 | no — registered, but never found as a `hash="..."` value attribute in real data; plausibly a child-object/reference-typed member (drives nesting rather than a plain scalar `<value>`), not investigated further |

This means the same technique (find the class's `ms_descriptor`, `get_xrefs_to` it, decompile whoever
reads/writes it, read the literal member-name strings straight out of the decompile) generalizes to
any other persisted record type — it isn't limited to the two classes checked this session.

**This also sharpens the earlier "mod compatibility" finding's mechanism.** `CPersistenceDB::RestoreEntity`
matches which record belongs to which live entity by **`EntityId`** (a hash lookup keyed on the
entity's own numeric world identifier — confirmed directly in its decompile), never by matching
`.fcb` name-hashes against `entitylibrary.fcb`'s own vocabulary. So "the engine finds what to
overwrite" via instance identity, not via any shared field-name signature between the save and the
data files — which is exactly consistent with the save's vocabulary being unrelated to
`entitylibrary.fcb`'s.

## `binary_classes.xml` resolves real savegame fields too, via a flat (not class-scoped) lookup

The per-class-scoped resolution described above (37%/14% of objects/values) is `FcbClassDefinitions`
working exactly as designed: an object's *own* resolved class's member list, walking its superclass
chain. That leaves every genuinely unresolved `hash="..."` — the outer persistence-wrapper layer this
whole section is about — completely untouched, by design (its objects never resolve to a known class
in the first place). Tested a different question: does the same hash, *ignoring which class declared
it*, still coincidentally appear as some member/class name somewhere else in `binary_classes.xml`'s
~1560 classes? Common short field names (`Id`, `Name`, `State`, `Pos`, `Flags`, `Category`, ...) are
plausible enough to be declared by some unrelated entity/vehicle/weapon class purely by coincidence of
English, and CRC32 doesn't care which class asked for the hash — same string, same hash, regardless of
context.

It does, substantially. Cross-referenced every distinct hash in one real save's exported tree against
a flat reading of the whole config file (every `<member name="...">`/`<class name="...">`, regardless
of nesting): **51 of 820 distinct value hashes and 6 of 246 distinct object-type hashes matched**, with
occurrence counts in the thousands for several. Verified each candidate two ways before trusting it:
(1) does the hash appear in real data at all, and (2) does the *byte length* in real data actually
match what the matched type requires (a hash match alone only proves the string coincides, not that
this specific field really carries that type) — implemented as `JackAll.App/SaveGames/SaveGameFieldCatalog.cs`,
a flat hash→(name,type) table built directly from `binary_classes.xml`, applied as a save-specific
retyping pass (real `type="Vector3"`/`"Int64"`/etc. with the bytes actually decoded, not just a name
label) rather than touching `FcbClassDefinitions`/`FcbXml` — same reasoning as the `Tag_*` dictionary
above, kept separate and display-only.

Concretely, checked across a real save's full ~2,300–5,000 per-field occurrence counts:

| Hash | Matched name (type) | Byte-length check |
|---|---|---|
| `0xB894A04C` | `Pos` (`Vector3`) | **100% pass** (781/781 exactly 12 bytes) — decodes to plausible world1 coordinates, e.g. `(2566.97, 2518.96, 18.49)`, same order of magnitude as the header's own player-position floats (Section 1) |
| `0x0F5E4BAA` | `EntityId` (`Int64`) | **100% pass** (2,773/2,773 exactly 8 bytes) |
| `0x9F448218` | `Enabled` (`Bool`) | **100% pass** (375/375 exactly 1 byte) |
| `0xFF3A7B97` | `Category` (`Hash`) | **100% pass** (2,340/2,340 exactly 4 bytes) |
| `0xFE11D138` / `0xDCB67730` | `Name` / `Value` (`String`) | variable-length as expected, decodes wherever null-terminated |
| `0x2ABD43F2` | `Id` (declared `UInt32`/`Int32` depending which class binary_classes.xml lists first) | **fails, correctly rejected** — real data is consistently 8 bytes (4,809/4,870), not 4; this hash is reused for a conceptually different, unrelated field in the save than whichever entity-library class declared "Id" as a 4-byte int, and the length check catches the mismatch instead of showing a wrong number |
| `0xCAC46EBE` | `Flags` (declared `UInt8`) | **fails, correctly rejected** — real data is consistently 4 bytes (885/885), not 1 |

The rejections are as important as the matches: they're proof the byte-length gate is doing real work,
not just decorative caution — "the hash matches a name binary_classes.xml knows" is demonstrably *not*
sufficient by itself, some of these are false friends. Other confirmed matches from the same pass
(not exhaustively re-verified for byte-length above, but hash-present in real data): `SectorId`
(`Int32`), `hid_DTCTH_ClassName`/`BoneId`/`sType`/`sContext`/`sMode`/`Type`/`texTexture`/`Bone`
(`Hash`), `MeshIndex`/`Index`/`Distance` (`Int32`), `hidPos`/`hidPos_precise`/`hidAngles`/`Position`
(`Vector3`), `vColor` (`Vector4`), `fWidth`/`fHeight`/`fLength`/`fAngle` (`Float`), `Usable`/`bEnabled`/
`hidConstEntity` (`Bool`), `PlanName`/`MoveToPlanName`/`SwitchWeaponPlanName` (`Hash` — AI behavior-plan
references), and object-type matches `LayerId`, `Resource`, `Part`, `Inventory`. `State` and
`Description` matched on **both** sides (as a value name here, as an object-type name in the earlier
`Tag_*` table) — the same string hashing identically in both roles isn't a bug, it's expected: the
persisted per-entity state blob is apparently both tagged `State` *and* typed as an object literally
named `State`.

**Practical tooling consequence**: JackAll.App's Saves tab applies both dictionaries in sequence —
`SaveGameFieldCatalog` (this section, full type+value decode from `binary_classes.xml`'s flat lookup)
first, then `SaveGamePersistenceTags` (the hand-curated `Tag_*`/`RegisterProperties` dictionary, name
only, for the handful of confirmed hashes `binary_classes.xml` doesn't know at all) — both kept out of
the shared, round-trip-critical `FcbClassDefinitions`/`FcbXml` machinery the Files tab's real
mod-editing depends on, since neither is verified as rigorously as `binary_classes.xml`'s own
provenance. `SaveGamePersistenceTags` no longer lists `Id`/`EntityId`/`State`/`Description` — the flat
catalog resolves all four with real type info now, making the bare name-only label redundant.

**This also sharpens the earlier "mod compatibility" finding's mechanism.** `CPersistenceDB::RestoreEntity`
matches which record belongs to which live entity by **`EntityId`** (a hash lookup keyed on the
entity's own numeric world identifier — confirmed directly in its decompile), never by matching
`.fcb` name-hashes against `entitylibrary.fcb`'s own vocabulary. So "the engine finds what to
overwrite" via instance identity, not via any shared field-name signature between the save and the
data files — which is exactly consistent with the wrapper layer's vocabulary being unrelated to
`entitylibrary.fcb`'s (even though, per the section above, plenty of *other* content in the same tree
does share that vocabulary, just via embedded component data, not via this matching mechanism).

**Partially answered**: `Tag_State`'s own nested payload — i.e. what a specific *entity type* (a buddy,
a weapon pickup, a vehicle, ...) actually saves as its dynamic state (position? health? alive-flag?).
The two record classes above are wrapper/bookkeeping objects (which hierarchy owns this record, how
much memory it uses, whether it's persisted) — the interesting per-entity-type gameplay state lives
one level deeper, registered by each entity class's *own* `RegisterProperties`, not
`CPersistenceDBRec`/`CBindingHierarchyDBRec`'s. The flat `binary_classes.xml` cross-reference above
turned out to answer part of this by accident, without needing to locate any specific entity class's
`RegisterProperties`: `Pos` (`Vector3`), `Enabled`/`Usable`/`bEnabled` (`Bool`), `Category`/`Type`/
`sType` (`Hash`), `SectorId` (`Int32`), and the AI-behavior `PlanName`/`MoveToPlanName`/
`SwitchWeaponPlanName` trio are all real, byte-length-verified fields somewhere in this nested state —
genuine per-entity dynamic-state fields, not wrapper bookkeeping. What's still missing is the
*mapping* from a specific entity type to *which* of these (and whichever unmatched hashes remain) it
actually uses, and the technique above (find a specific class's `ms_descriptor`, `get_xrefs_to` it,
decompile, read the literal names) still generalizes for filling that in deliberately rather than by
accident, for any entity class of interest.

## Correction: `MemoryUsage` is registered independently by *two* record classes, not one

The "confirmed member names" table two sections up reported `MemoryUsage` at 4,488 occurrences without
explaining why that doesn't match either record class's own instance count (`HierarchyRecord`=2,154,
`Record`=2,651). Re-decompiled both `RegisterProperties` bodies directly this session
(`CPersistenceDB::CBindingHierarchyDBRec::RegisterProperties` and
`CPersistenceDB::CPersistenceDBRec::RegisterProperties`, both re-confirmed live via
`decompile_function_by_address` against `0x09679b22`/`0x09679a93`) rather than trusting the earlier
summary: **both classes independently call `PushBackMember` with the literal name `"MemoryUsage"`** —
they are two unrelated members on two unrelated classes that happen to share a name (and therefore a
hash). `CBindingHierarchyDBRec` also registers `PersistType` (`0x60` byte offset) and `BindingHierarchy`
(child-object, still never observed as a plain value); `CPersistenceDBRec` registers only `MemoryUsage`.
Their instance counts **sum** to the observed total: 2,154 (`CBindingHierarchyDBRec`, one per
`HierarchyRecord`) + 2,334 (`CPersistenceDBRec` instances found via the `EntityId`→hierarchy-entry
lookup inside `SaveDB`, i.e. records that *do* belong to some binding hierarchy) = 4,488. This also
explains why `PersistType` (2,154) and `HierarchyId` (2,334, see below) don't match each other's counts
either — they belong to the two different classes in that same split.

## New: where a persisted entity's own dynamic state actually gets captured

The "Not yet traced" list below used to ask where `Tag_State`'s nested payload — the actual
per-entity-type gameplay state (position, health, buddy loyalty, ammo, ...) — comes from, since
`CPersistenceDB::SaveDB` only *writes out* an already-captured `ISerializableNode`, it doesn't build one.
Traced the write side this session: **`CPersistenceDB::AddRecord(TEntityHandle<CEntity>, EPersistType,
CBindingHierarchyDBRec*)`** (`FarCry2_server @ 0x09679e90`, decompiled) is the answer. After allocating a
new `CPersistenceDBRec` and inserting it into the DB's hash table, it does:

```c
uVar13 = (**(code **)(*(int *)pCVar15 + 0x10))(pCVar15);           // pCVar15 = the live CEntity
CNomadObjectDescriptor::SaveState((CNomadObject *)uVar13, (ISerializableNode *)((ulonglong)uVar13 >> 0x20));
```

i.e. it calls the entity's **own polymorphic vtable slot `+0x10`** (an accessor — plausibly
`GetObjectDescriptor()`-shaped, returning a descriptor+node pair packed into the 64-bit return value the
same way `CPersistenceDBRec::RegisterProperties`'s own `PushBackMember` calls above return one) and feeds
that straight into the *generic* `CNomadObjectDescriptor::SaveState` — the exact same engine-wide
reflected-property serializer already documented for the `.fcb` writer
([the `.fcb` format page](./fcb.md)). This confirms: **there is no savegame-specific "capture this entity's
state" function** — every entity class captures its own persisted state purely by having already called
`RegisterProperties`/`PushBackMember` for whatever fields it wants captured (the same mechanism every
`entitylibrary.fcb`-shaped data class uses), and `AddRecord` just triggers that generic machinery,
recursively, for every child `CEntityProxy` too (the tail of the function walks `pCVar15+0x68`'s proxy
list and re-calls `AddRecord` on each one). This is what `CGhostManager::OnFinalize` (confirmed earlier)
eventually calls, mirrored by `RestoreEntity`'s `LoadState` on the read side.

Practical upshot: the ~250-350 anonymous `RegisterProperties` functions in `FarCry2_server` (one per
entity/component class, none individually named beyond the short method name — a plain function search
can't tell them apart or attribute a found string to "which class") are *exactly* where every
per-entity-type field in a save's `Tag_State` payload gets its name and comes from. Individually
decompiling all of them to attribute names to classes wasn't attempted this session (a few — `CVehicle`,
`CWeapon` — were spot-checked; none had an obviously-named `RegisterProperties` call in their own
constructor, meaning the call site is elsewhere, not chased further). The dictionary-attack technique
below sidesteps needing to, at the cost of not knowing *which* entity type owns which recovered name.

## New: recovering ~800 more names by dictionary attack instead of per-class decompiling

Given the above, decompiling one `RegisterProperties` at a time to grow the "confirmed member names"
table further doesn't scale (300+ candidates, no name/namespace to filter by in a plain function search).
But every field name reaching `PushBackMember` is passed as a literal `const char*` — meaning **the name
string exists verbatim in `FarCry2_server`'s own rodata/symbol-table strings regardless of which
function's disassembly you'd have to read to find it**. So: pulled the *entire* string table out of the
live Ghidra project via the MCP bridge's `list_strings` (paginated — the binary is unstripped and has
tens of thousands of strings; ~92,000 lines/~82,000 distinct strings were pulled this session, covering
roughly the first 190,000-offset range of the table, not exhaustively to the end), CRC32-hashed every
candidate that looks like a plausible C-identifier (`^[A-Za-z_][A-Za-z0-9_]{1,63}$`) the same way the
engine hashes everything (`FcbClassDefinitions.Crc32Ascii`-equivalent), and kept only the ones whose hash
exactly matches a hash **actually present** in a real save's fully-exported, unresolved `PersistenceDB`
tree (`tmp/savegame.fcb.xml` — 819 distinct value hashes, 246 distinct object hashes, 1,046 distinct
total, counting occurrence frequency and per-hash byte-length consistency along the way).

Result: **792 additional hashes resolved to an unambiguous name** (zero CRC32 collisions among the
matched candidates — every matched hash had exactly one candidate string producing it). Combined with the
9 hand-curated `SaveGamePersistenceTags` entries and the 68 `binary_classes.xml` flat-lookup matches
already documented above, **843 of 1,046 distinct hashes in this one real save (80.6%) now resolve to a
real name** — up from the 37%/14% class-scoped and ~6% flat-lookup figures reported earlier in this file.
Some notable recovered names, cross-checked against their observed byte length and occurrence count for
plausibility (all 100%-consistent-length across every occurrence in the sample, though — same caveat as
`SaveGameFieldCatalog` — a hash match is not proof of semantics, only of the literal string):

| Hash | Name | Count | Byte len | Plausible read |
|---|---|---|---|---|
| `0x7A33C5A1` | `KeyType` | 15,510 | 4 | ubiquitous — likely on every generic key/value pair node in the tree |
| `0xB083E9A2` | `ValueType` | 11,191 | 4 | sibling of `KeyType` |
| `0x725BF5BD` | `WorldMatrix` | 2,651 | 64 | 4x4 float matrix, one per `HierarchyRecord` |
| `0x542EF648` | `CurrentHealth` | 1,606 | 4 | float |
| `0x802B3E7F` | `hidLastVelocity` | 1,338 | 12 | Vector3 |
| `0xA463782E` | `ScriptCallbackId` | 802 | 8 | |
| `0x83B1F50E` / `0x5276954B` | `CachedAnchorPosition` / `CachedAnchorOrientation` | 322 | 12 / 16 | Vector3 / Vector4 |
| `0x1C97590F`..`0xD56EB6C6` (cluster of 61) | `MaxReliability`, `JammingBullet`, `RememberedAmmoInClip`, `RememberedAmmoOverflow`, `CurrentBulletSpread`, ... | 61 each | | a weapon-memento-shaped cluster (buddy weapon state?) |
| `0x519DEE85`..`0x606B06FE` (cluster of 54) | `UsingLook`, `AimAngles`, `BarkLookAngles`, `FovOverride*`, `ReactionMoveId`, ... | 54 each | | an AI look/animation-state cluster |
| cluster of 27 (dozens of hashes) | `CurrentArmyMemberState`, `ThreatLevel`, `AlertLevel`, `IsPlayerInAIvsAIZone`, `MercBrain`, `BuddyDownEnable`, ... | 27 each | | a full AI-brain/army-member state block — almost certainly one buddy/merc NPC's complete behavior state |
| `0xB3056FD2` | `DiamondReward` | 27 | 1 | bool |
| root-object names (object-hash matches) | `CampaignSave`, `PersistenceDB`, `BuddyManagement`, `MissionManagement`, `GameplayManagement`, `WorldDiamonds`, `DiamondsData`, `MainHud`, `BlueArmy`/`RedArmy`/`GreyArmy`/`NeutralArmy` | 1 each | | top-level save-data category nodes — the save's overall shape, not just entity records |

Also recovered several likely level/act-design string constants (occurrence count 1, not observed as
values but their hashes are present as object/value hashes near the file's very start, alongside the
already-known `CScreenShot` section) — `FIRST_LT_WARLORD`, `START_FACTION_BOSS`,
`PRIMARY_BUDDY_NAME`/`SECONDARY_BUDDY_NAME`, `MISSION_BUDDY_A1LM01..A3SM15`, `WINNING_FACTION_*` /
`LOSING_FACTION_*` — these read as campaign-scripting constant/enum names (Act 1/Act 2 faction-war
bookkeeping), consistent with `CCampaignGameFileHeader`'s still-unidentified trailing 3 `u32`s
(difficulty/act/chapter, Section 2 above) living in the same conceptual area of the save.

**Practical tooling consequence**: implemented as
`JackAll.App/SaveGames/SaveGameCompiledFieldNames.cs`, loading a flat `hash\tname` table shipped at
`tools/JackAll/assets/savegame_field_names.tsv` (792 rows, generated this session; the 9
`SaveGamePersistenceTags` and the `binary_classes.xml`-covered hashes are deliberately excluded from the
table to avoid two annotation passes emitting a duplicate `known="..."` attribute on the same element).
Wired into `SaveDetailsViewModel.LoadAsync` right after `SaveGamePersistenceTags`, before the BinHex
string decoder — name-only, exactly like `SaveGamePersistenceTags`, no wire type asserted.

The technique itself generalizes: any future session can re-run it against a wider string-table page
range (this session did not reach the end of `FarCry2_server`'s string table — pagination was stopped
partway for time, not because it was exhausted) or against a different real save to grow the table
further, without needing to identify which of the 300+ `RegisterProperties` functions a name belongs to.

## Follow-up: scanning the raw binaries directly gets to 97.8%, no Ghidra needed for this part

The 80.6% figure above only used strings pulled through the GhidraMCP bridge (`list_strings`, paginated,
one HTTP round-trip per 2,000 strings — slow, and only covered part of `FarCry2_server`'s table). Realized
the bridge isn't actually necessary for this step: `PushBackMember`'s name strings are plain bytes sitting
in the binary's own rodata/`.strtab`, so a straightforward "find every run of ≥4 printable-ASCII bytes"
scan directly over the **files on disk** finds the same strings (and more, exhaustively, in one pass)
without any Ghidra round-trips at all. Located both source binaries directly:

- `tools/FarCry2_Dedicated_Server_Linux/bin/FarCry2_server` (52 MB, the same ELF the Ghidra project has
  imported) — scanning it directly and exhaustively (328,561 raw ASCII runs) finds strings the partial
  MCP-paginated pull missed.
- `C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\Dunia.dll` (20 MB, the PC client engine —
  see [the engine overview](../engine-internals/overview.md)) — an **independent second source of the same literal strings**, since
  the PC client and the Linux dedicated server both compile from the same shared engine source and
  therefore embed the same `PushBackMember("SomeFieldName", ...)` call sites, even though `Dunia.dll`'s
  own *functions* were never demangled/named (irrelevant here — raw string bytes don't need symbol
  recovery to read).

A ~100-line Python script (raw byte scan + `zlib.crc32` + match against the real save's hash set) found
**233 more unambiguous matches** in seconds, no MCP calls at all. After removing hashes that overlap
`SaveGameFieldCatalog`'s existing `binary_classes.xml` resolution (61 of the combined 1,025 rows — mostly
short common names like `Value`/`Name`/`State`/`Category`/`hidPos` that this dictionary attack also
independently found, cross-validating the two methods against each other), the final tally:

| Source | Distinct hashes resolved |
|---|---|
| `SaveGamePersistenceTags` (hand-decompiled) | 9 |
| `SaveGameFieldCatalog` (`binary_classes.xml`, byte-length verified) | 50 |
| `SaveGameCompiledFieldNames` (dictionary attack, MCP strings + raw binary scan combined) | 964 |
| **Union, this one real save** | **1,023 / 1,046 = 97.8%** |

23 distinct hashes remain unresolved in this sample (`2ABD43F2`/`Id` — already known by name but rejected
by `SaveGameFieldCatalog` for a byte-length mismatch, see the section above — plus `B2DDED49`, `2574E181`,
`FFFFFFFF`, `FA6F25A3`, and ~18 others with no matching string found in either binary's printable-ASCII
runs). Getting the rest would mean either a smarter candidate generator (current pass only tries strings
that already exist verbatim and match `^[A-Za-z_][A-Za-z0-9_]{1,63}$`, so anything using an unusual
character, a very short/generic name that got filtered as ambiguous, or genuinely absent from either
binary's static strings — e.g. dynamically-built at runtime — would be missed) or brute-force diffing two
saves that differ by one known action.

**Practical tooling consequence, updated**: `savegame_field_names.tsv` now ships 964 rows (up from 792).
The dedup step against `SaveGameFieldCatalog` was redone properly this round — checking real
byte-length-verified member matches *and* any `binary_classes.xml` class name, rather than a hand-picked
exclusion list — so a handful of rows from the very first pass that had the same undetected overlap
(`hidPos`, `BoneId`, `MeshIndex`, and others) were also cleaned up here, not just the new raw-scan finds.

## Validated: mod compatibility with existing saves is a per-property overlay, not a full freeze

**See [the entity-library overlap section below](#entity-library-overlap) for the follow-up that
measures this concretely** — this section establishes the *mechanism* (below); that file cross-references
a real save's fully name-resolved tree against `binary_classes.xml` to answer, with real numbers,
*which* `entitylibrary.fcb` classes/fields the mechanism actually touches (237 classes, 574 members, in
one sample save).

Follow-up investigation (same session) to answer a concrete question: if a mod changes
`entitylibrary.fcb`, does an existing save still show the old values for entities it has already
persisted — making mod changes to persisted entities effectively "worthless" as reported anecdotally?
Traced the actual entity-restore path in `FarCry2_server` rather than guessing from the `SaveDB`
write side alone.

**`CPersistenceDB::RestoreEntity(TEntityHandle<CEntity>)`** (`@ 0x0967c050`, decompiled) is called on
an **already-constructed, already-locked `CEntity`** — its first two lines are
`pCVar1 = *(CEntity**)(*param_2 + 0xc); CEntity::Lock(pCVar1);`, i.e. the entity object already
exists in memory by the time this function runs. It then does two hashtable lookups by `EntityId`
(one against `this+0x14`, a binding-hierarchy table; one against `this+0x30`, a persistence-record
table) and, **only if a record is found**, calls `CNomadObjectDescriptor::LoadState(entityObject,
persistedNode)` — the same generic reflected-property loader used everywhere else in the engine's
`CNomadObject` system (documented for the `.fcb` reader in [the `.fcb` format page](./fcb.md)'s
`GenericMember`/`BasicTypeHandler` machinery). If no record exists for that `EntityId`, `RestoreEntity`
does nothing at all and returns.

Confirmed the call site: **`CGhostManager::OnFinalize(TEntityHandle<CEntity>)`** (`@ 0x096c3930`,
decompiled) — the entity finalization hook, i.e. the tail end of normal entity spawn/construction —
calls `CPersistenceDB::RestoreEntity` as its very last step, on the entity it just finished
finalizing.

**This settles the ordering**: an entity is always spawned/constructed first from its current, live
entity-library definition (wherever that spawn logic lives — not itself retraced this session, but
nothing upstream of `OnFinalize` reads from the persistence DB), and *afterward*, `RestoreEntity`
conditionally overlays whatever specific properties got captured in that entity's persisted record —
if one exists at all. This is a **property-level overlay on top of a freshly-spawned object**, not a
wholesale substitution of the entity's definition, and not even applied to entities with no persisted
record in the first place.

Practical consequences for "can I patch `entitylibrary.fcb` and have it affect an existing save":

- **Entities never persisted at all** (no record for that `EntityId` in the save's `PersistenceDB`
  dump — plausible for most of the map on any given save, since only entities that actually changed
  state get a record per the engine-architecture notes in `research/knowledge.md` §5) spawn 100% from
  current data. A mod's changes apply immediately and fully, no save-editing needed.
- **Entities that do have a persisted record** only have `LoadState`-applied properties overridden —
  and only the properties that `CNomadObjectDescriptor`'s registered schema for that record type
  actually captures (this session didn't enumerate that per-type property list — see "Not yet
  traced"). Design-time tuning values that live only on the entity/weapon *archetype* and aren't part
  of an instance's dynamic persisted state (plausible candidates: weapon damage/range/reliability
  curves, AI perception parameters — anything not describable as "this specific entity's current
  state") would very plausibly still take effect even for a persisted entity, since `LoadState` never
  touches whatever it wasn't given data for. Genuinely dynamic per-instance state that *is* captured
  (position, health/alive-state, inventory contents, hierarchy relationships) would remain frozen at
  whatever value existed when it was persisted, regardless of later `entitylibrary.fcb` edits.
- The originally-reported "known behavior" (huge chunks of `.fcb` content are frozen in the save) is
  **not wrong, but is entity-and-property-scoped, not global** — it doesn't mean the whole save is
  immune to `.fcb` edits, only the specific already-touched entities and already-captured properties.
- This also suggests a much simpler mitigation than a full value-level merge tool: since
  `RestoreEntity` is a no-op when no record exists, **deleting an entity's persisted record from the
  save's embedded `.fcb` tree entirely** (rather than trying to reconcile individual values) would
  force that entity to respawn purely from current `entitylibrary.fcb` data next load — a
  coarser-grained but far more tractable technique than field-level reconciliation, and one that
  doesn't require knowing what any given `nameHash` means. Not implemented or tested this session.

## New: why the filename itself is a bare number (`<numbers>.sav`)

Confirmed directly via `FarCry2_server`'s symbol table (same shared-engine caveat as the rest of this
file — traced in the Linux server binary, not `Dunia.dll`, but this is generic filename-generation
code with no server/client-specific behavior, so it applies to the PC writer too):

- **`GameFileUtils::GenerateCampaignGameFileName(CryStringBase<char>&)`** (`0x091ea6b0`) is the
  function that produces the `<digits>.sav` name. It does **not** use wall-clock time, a save-slot
  index, or any player-visible identifier. Instead:
  1. `CHighPerfTimer::GetTimeValue()` (`0x09c6ea70`) → `Gear::Time::GetCpuCycle()` (`0x0a0b9ed0`) →
     a bare **`rdtsc()`** instruction — the raw CPU timestamp-counter register, a free-running cycle
     count with no calendar meaning.
  2. `ndRandUInt()` (`0x09c6e8a0`) — a small in-engine PRNG (`globalRandom = globalRandom*0x343fd +
     0x269ec3`, returning `(globalRandom >> 16) & 0x7fff`, a classic 15-bit linear-congruential
     generator) is added on top as jitter.
  3. The sum is `sprintf`'d as a plain decimal integer via `"%I64d.%s"`, with `g_kGameFileExtension_
     Campaign` (a `GameFileUtils` static, matching the real `.sav` extension) appended, then handed to
     `GenerateRelativeFileName` to become the actual save-folder-relative path.
  - **`CFCXEditorGameFilesService::GenerateSaveFileName`** (`0x08a41a50`, custom-map/editor path) is a
    sibling that instead calls `GameFileUtils::GenerateCustomMapFileName` — not decompiled this
    session, but presumably the same pattern given the shared `GameFileUtils` namespace.
- **So `178430170947.sav`'s name is "RDTSC cycle count + small random offset," not a timestamp, save
  slot, or hash.** This is a cheap, collision-resistant unique-ID scheme, not a meaningful identifier —
  there's no reason to expect the number to be sortable, parseable as a date, or stable across
  machines/reboots. (The magnitude of the real sample — ~1.78×10^11, i.e. only ~tens of seconds' worth
  of cycles at GHz-class clock speeds — implies the counter isn't a raw since-boot value on real
  hardware; whether `GetCpuCycle` is actually reading a per-process/virtualized/offset TSC rather than
  the true since-power-on one is unresolved and doesn't change the practical conclusion above.)

## Not yet traced

- **`CGameFileHeader`'s own `WriteToFile`/`ReadFromFile`** — not located under that literal name in
  `FarCry2_server`'s symbol table (only `GetSaveSize` and the constructor were found this session);
  the field semantics in Section 1 above are inferred from real bytes + plausibility, not decompiled
  proof. Finding this function (likely inlined into `CCampaignGameFileHeader`'s own written-to-file
  logic given the header's writes and the two strings appear contiguous in the file) would upgrade
  those from hypothesis to confirmed.
- **The `CCampaignGameFileHeader` trailing 3 `u32`s' exact meaning** (difficulty/act/chapter guessed,
  not confirmed) — no accessor was traced from the class to a named concept.
- **The lone `u32 = 0` field between the DLC list and the `.fcb` blob** (`GetSaveSize`'s `+ 4`
  constant) — present and measured, purpose unknown.
- **Screenshot pixel channel order** (RGBA vs BGRA) — not distinguished from this one sample.
- **Where `PlayerPos` (predicted in `command_line_args.md`) actually lives** — very plausibly the
  three Section-1 floats (see above), but not cross-checked against a `-load`-and-read-back live test
  or a decompiled accessor.
- **The rest of the embedded `.fcb` tree's schema** — **largely addressed this session** via the
  dictionary-attack technique above: 80.6% of distinct hashes in the sample save now resolve to a real
  name (up from a single hand-walked value, `"Addi Mbantuwe"`, in the original pass). What's left is the
  remaining ~19.4% (203 distinct hashes) that didn't match any string pulled from the ~190,000-offset
  range of `FarCry2_server`'s string table covered this session — re-running the same technique against
  the rest of the table (not exhausted, just not finished for time) is the obvious next step, no new
  method needed. Brute-force diffing two saves that differ by one known in-game action remains untried
  and would be the way to attach *meaning* (not just a name) to a resolved-but-still-opaque field.
- **Whether the container format (the 4-section wrapper) is identical for quicksaves vs. manual saves
  vs. checkpoint autosaves** — only one save file was inspected byte-for-byte this session; the other
  ~30 files in the same folder were not diffed against it.
- **What decides whether a given entity ever gets a persisted record at all** — the actual condition
  under which `CPersistenceDB::AddRecord`/`AddHierarchy` get called for an entity wasn't traced this
  session (only the read/restore side, `RestoreEntity`, was). "Only entities that changed state get
  persisted" is carried over from `research/knowledge.md`'s community/developer-sourced theory, not
  independently re-derived from `FarCry2_server` disassembly.
- **`CNomadObjectDescriptor`'s per-record-type registered property schema** — the *mechanism* is now
  confirmed (`CPersistenceDB::AddRecord` calls the live entity's own vtable+`0x10` accessor into generic
  `CNomadObjectDescriptor::SaveState`, see above), and the dictionary attack recovered plausible field
  *names* for large blocks of this state (the 27-count and 54-count clusters above read as a single
  AI/army-member brain and a look/animation-state block respectively). What's still missing is which
  specific fields `RegisterProperties` covers *per named entity class* (i.e. attributing a recovered name
  to "this is `CBuddy`'s" vs. "this is `CVehicle`'s") — the 300+ anonymous `RegisterProperties` functions
  were not individually decompiled/attributed this session (see the section above). This is the concrete
  next step for anything that wants to reason precisely about which mod edits would vs. wouldn't take
  effect on an existing save, rather than the plausibility argument given above.
- **The entity-spawn path itself, upstream of `CGhostManager::OnFinalize`** — confirmed *that*
  entities are constructed before `RestoreEntity` runs, but the actual code that reads
  `entitylibrary.fcb`/resolves an entity's archetype at spawn time wasn't retraced in this
  session (it's already partially covered for the `.fcb` *reading* side in
  [the `.fcb` format page](./fcb.md) and [the archives format page](./archives-fat-dat.md), just not
  specifically re-connected to this spawn call chain).

## Entity-Library Overlap

A direct follow-up to the savegame format section above, which traced the container format and the
`CPersistenceDB::AddRecord`/`RestoreEntity` overlay mechanism but stopped short of measuring *which*
`entitylibrary.fcb` classes/fields that mechanism actually touches in a real save. This section
answers that with real numbers from one real save, for anyone modding `entitylibrary.fcb` who needs
to know which of their edits an existing (already-played) save would silently ignore.


### Status: measured against one real save, name-resolved via JackAll's Saves tab

Made possible by the savegame format section above's dictionary-attack work (which pushed name
resolution in the Saves tab's rendered tree to 97.8%) and the `SaveGameXmlRenderer` rewrite that made the
Saves tab emit the *same* `type="..."`/`name="..."` shape as an ordinary resolved `.fcb` — see
`tools/JackAll/src/JackAll.App/SaveGames/SaveGameXmlRenderer.cs`. That shared shape is what makes this
measurement possible at all: it lets a save's object/value names be cross-referenced against
`tools/JackAll/assets/binary_classes.xml` (the real `entitylibrary.fcb` class/member vocabulary) by plain
string equality, no separate hash-matching step needed.

**Method**: exported one real save's full `PersistenceDB` tree via the Saves tab (`178430170947.sav`,
same sample `savegame_format.md` uses), then streamed it (`xml.etree.ElementTree.iterparse` — the
rendered tree is ~14.7MB/238k lines, too big to parse as one DOM comfortably) counting, for every
`<object type="X">` whose `X` is a real `binary_classes.xml` class name, which `<value name="Y">` names
appear as its direct children and whether `Y` is *also* a real `binary_classes.xml` member name.

### The mechanism, recapped

(Full trace in `savegame_format.md`'s "mod compatibility" section.) An entity always spawns first from
whatever `entitylibrary.fcb` currently says — your edits apply immediately and fully, unconditionally, as
the starting point for every entity. **Only if that specific entity instance already has a
`PersistenceDB` record** (i.e. something about it changed during that save's playthrough) does
`CPersistenceDB::RestoreEntity` run afterward and overlay whichever *specific properties got captured*
on top of the freshly-spawned object — nothing else. So the classes/fields below aren't "unsafe to mod" in
general; they're "the specific set of properties that, for an entity a player has already touched in an
existing save, will keep showing that save's frozen value until the record is cleared or a fresh instance
spawns." A new game, or any entity nobody has interacted with yet, always gets your edit.

### Headline numbers (this one save)

- **237 distinct real `entitylibrary.fcb` classes** appear instantiated inside `PersistenceDB` — every one
  a genuine class from `binary_classes.xml`, reused verbatim because it's the same live C++ component tree
  being serialized (see `savegame_format.md`'s "`binary_classes.xml` actually DOES resolve a real chunk of
  this content" section for why that's expected, not a coincidence).
- **574 distinct real `entitylibrary.fcb` member names** show up captured as per-instance state somewhere
  under those classes.
- For comparison: the save's `PersistenceDB` tree overall has 474 distinct object-type names and 1,319
  distinct field names total — so the real-`entitylibrary.fcb`-overlapping subset above (237/474 classes,
  574/1319 fields) is roughly half by class count, well under half by field count. The rest is the
  savegame's own disjoint wrapper/computed-state vocabulary (`Tag_*` structural tags, AI runtime state
  like `ThreatLevel`/`AlertLevel`, and similar — see `savegame_format.md`), which has no
  `entitylibrary.fcb` design-time counterpart to "override" in the first place.

### High-impact classes for modders

| Area | Class(es) | What's frozen for an already-touched instance |
|---|---|---|
| Vehicle handling/tuning | `CVehicle` + `Reliability`/`Sound`/`WheeledParams`/`Steering`/`Rumble`/`Gear0-2`/`SoundSettings`/`ParticleSettings`/`GaugesSettings`/`VehicleLightSettings`/`DustParticles` | Almost every tunable vehicle parameter that exists — **the single biggest risk category**: `fAccelerationPushFactor`, `fBigCollisionSpeed`/`fMediumCollisionSpeed`/`fMinCollisionSpeed`, `iRepairSteps`, `vehicleMaxLookAngle`, full gearbox/suspension/reliability-curve tuning |
| AI movement/behavior tuning | `CGameAgent`, `CPawnAgent`, `Body` | `fSpeedsRun`/`fSpeedsSprint`/`fSpeedsWalk`, accelerations/decelerations, `fJumpHeight`, swim/walk speeds, AI-sense thresholds (`m_*ClearVal`/`m_*FuzzyVal`) |
| Visuals | `CGraphicComponent` (705 instances) | `bCastShadow`, `bReceiveShadow`, `fLODSphereRadius`, `hidMeshName`, `objModel`, `agAmbientGroup`, `olgLightGroup`, LOD/reflection/ambient flags |
| Physics | `CCompoundPhysComponent`/`CRigidPhysComponent`/`CStaticPhysComponent`/`CVehicleWheeledPhysComponent` | collision flags (`bUseFastCollision`, `bCreateAsStatic`), resource refs |
| Starting loadout | `Inventory` (31 instances) | `bAutoDraw`, `bAutoReload`, `bUnlimitedAmmo`, `packInventoryPack`, `sInitialWeaponCategory`, `archGPSVehicleArchetype` |
| Pickup availability | `CPickupWeapon`/`CPickupDiamond`/`CPickupHealth`/`CPickupMissionItem`/`CMedicStation`/`CPickupPile`/`COpeningPickup` | `bPickable`, `bCanBeScouted` (and `CPickupWeapon` also `iMaxAmmo`/`iMinAmmo`) |
| Near-universal | almost every `C*Component` | `hidHasAliasName` (identity flag), `Enabled`/`bEnabled`/`Enable`-shaped toggles, `Category`/`Name` (via `LayerId`) |

**Good news for weapon balance mods**: `CFCXWeapon` (the live weapon-instance component) only overlaps on
`iAnimationValue`. None of the actual ballistics classes (`CWeaponFireBulletProperties`,
`CWeaponFireProjectileProperties`, `CWeaponPropertiesCommon`, etc. — see `fcb_format.md`'s survey of
`entitylibrary.fcb`'s own vocabulary) appear captured **at all** in this save's `PersistenceDB` tree.
Damage/range/reliability-curve balance values are archetype-only, always read fresh at spawn — safe to
edit even for a save where that weapon's already been picked up.

### Per-class breakdown

Every real `entitylibrary.fcb` class found instantiated in this save, most common first, with its exact
captured field list split into "real `binary_classes.xml` member — a design-time value that gets frozen"
vs. "savegame-only name — genuinely dynamic/computed per-instance state with no design-time counterpart,
not a modding concern in the same sense."

Streamed from `tmp/savegame.xml` (73282 total `<object>` elements, 474 distinct type names, 1319 distinct field names) against `tools/JackAll/assets/binary_classes.xml` (2025 class names, 2009 member names).

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
  record — and therefore which classes/fields appear in a measurement like this at all — depends entirely
  on what that specific playthrough touched. A longer save, a different route through the campaign, more
  buddies rescued, more vehicles driven, would very plausibly freeze a larger and slightly different
  subset. Treat the 237-class/574-member list as "confirmed real, at minimum this much," not "the complete
  set of everything that can ever be captured."
- A name appearing in the "design-time fields also captured" list means *this save's copy* of that
  specific entity is frozen for that field — not that every entity of that class in every save is frozen.
  An entity nobody has interacted with in a given save is unaffected regardless of class.
- A hash match between a save's captured field and a `binary_classes.xml` member name is strong evidence,
  not proof (same caveat `savegame_format.md`'s `SaveGameFieldCatalog` section already documents) — CRC32
  collision between two unrelated 32-bit values is vanishingly unlikely at this sample size, but not
  formally impossible.
- The "savegame-only fields" lists are not necessarily irrelevant to modding — some (e.g. `Inventory`'s
  `CurrentWeapon`/`DesiredWeapon`, `AIObject`'s dozens of AI-state fields) are genuinely dynamic runtime
  state with no design-time equivalent to override in the first place, but a few (e.g. `Health`/`Stamina`'s
  `MaxValue`) are plausibly an *internally differently-named* mirror of a real design field this pass
  couldn't match by literal string equality — see "Not yet traced" below.

### Not yet traced

- **Attributing a captured field block to a *specific* entity class**, not just the shared component class
  that captured it. E.g. the AI-tuning cluster under `CGameAgent`/`Body` applies to whichever specific
  pawn archetypes reference those component instances — this pass only reached "these fields on this
  *component* class get frozen," not "here's the full list of playable/NPC archetypes that use it."
  `reverse/dunia/savegame_format.md`'s technique for finding a specific class's `RegisterProperties`
  (`get_xrefs_to` its `ms_descriptor`) generalizes to this if a specific archetype needs confirming.
- **Whether "savegame-only" fields with a plausible design-time cousin (`MaxValue` on `Health`/`Stamina`,
  `CurrentHealth` on several phys components) are actually an aliased/renamed mirror of a real
  `entitylibrary.fcb` field**, rather than genuinely archetype-less runtime state. Checking would mean
  finding the specific component's `RegisterProperties` and comparing its full registered member list
  against what `entitylibrary.fcb` declares for the same conceptual class, rather than the flat
  string-equality check this pass used.
- **Re-running this measurement across the other ~30 save files** in the same `Saved Games` folder (per
  `savegame_format.md`'s own "Not yet traced" list) to see how much the 237/574 figures actually move
  between playthroughs, and whether any classes/fields are load-bearing across every sample (i.e.
  captured in *every* save regardless of playthrough) versus only showing up in longer/more-completionist
  ones.

### Reproducing this

The analysis is a single streaming pass, not tied to any particular save: export a save's `PersistenceDB`
tree via JackAll's Saves tab (or `JackAll.Cli`'s save-reading path), then cross-reference every
`<object type="X">`/`<value name="Y">` pair against `tools/JackAll/assets/binary_classes.xml`'s own
`<class name="X">`/`<member name="Y">` declarations. No hash-matching or Ghidra access needed for this
part — it's pure string-equality now that the Saves tab renders real names via `SaveGameXmlRenderer`.
