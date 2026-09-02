---
sidebar_position: 3
---

# `depload.dat` — Dependency Chunk

:::info[Verified via reverse engineering, corrects an earlier community write-up]
Originally documented community-side (Discord, Far Cry Modding Community, 2026-04-09 — a from-scratch
black-box exchange between **fdx4061** and **ArmanIII**; not supported by FCBConverter, hence the
from-scratch approach: *"fcb converter doesn't support fc2 depload.dat"*). Independently confirmed and
extended by disassembly, traced live via GhidraMCP against the same more heavily-symbolized
`FarCry2_server` binary used for the [DLC entity-library merge](./archives-fat-dat.md). See
[intro](../intro.md) for how RE-verified and community-reported claims are distinguished on this site.
:::

`depload.dat` records, per world, which resources a resource depends on (its "parents"/"children") —
a **prefetch manifest**, used by the engine to warm what a resource will need. It is also, for
animation, the membership list of every `CAnimationPackageResource`: a `.mab` the engine will play
has to be listed under the package that plays it, so for clips this file is not a hint but a
requirement.

:::note[It does not gate loading — for a texture]
A *texture* absent from every `depload` still resolves and renders when something asks for it —
verified with one staged into `patch.dat` at a path present in no shipped archive, no hashlist and
no `depload` list, reached through a material that *is* listed. For that case, absence costs
streaming warmth, not availability.

**This does not generalise. See [Animations are not like textures](#animations-are-not-like-textures)
below** — an animation clip in the same situation did *not* load.
:::

## How it's loaded

`CXGame::LoadDepLoad` (`0x08888d50`) is the entry point:

1. Calls `CWorldDescriptorImpl::LoadDep()` (`0x09c2b0c0`), which loads the current world's own
   `<world>/generated/<world>_depload.dat` (format string `"%s%s_depload.dat"`), and its sibling
   `"%s%s_deploadnewparticles.rml"` — a second, RML-format dependency file for particle effects. RML
   is a separate container and a solved one: `jackall-cli rml decode` / `rml encode` round-trip
   `oasisstrings.rml` byte-identically at 946 KB. Its *contents* are out of scope here. If the binary
   `.dat` isn't found, it falls back to a same-named `_depload.xml` (a plain `XmlParser::parse` path).
   Nearly every world ships that XML twin beside its binary — 25 of them, up to 5.3 MB — and they
   carry the same parents the binary does, with names restored; see
   [The type table](#the-type-table). Whether the loader's fallback actually consumes that exact
   shape is untested, since the binary is always present.
2. Then walks every installed DLC via `CDlcService::GetDepLoads()` and loads each one's own
   `depload.dat` the same way, with `isPrimary = false`.

Both paths bottom out in `CResourceManager::LoadDep(IFile*, bool isPrimary)` (`0x09c07f50`) →
**`CResourceDataBase::LoadBinaryFile(IFile*, bool isPrimary)`** (`0x09c594c0`), the actual binary
reader — its `IFile::Read` call sequence is the ground truth for the layout below. `isPrimary` only
changes how loaded records merge into the in-memory resource database; it has no effect on the file's
own byte layout.

## Structure

A file-level header, then a parents array:

```
offset  size  field
0       4     parent count N (u32)
4       8*N   N × parent entries
```

Each parent entry — confirmed field-for-field against the three separate `IFile::Read` calls
(`2`, `2`, then `4` bytes) `LoadBinaryFile` issues per entry:

```
offset  size  field
0       2     childIndex — index (not byte offset) into the flattened child arrays below
2       2     childCount — how many consecutive entries starting at childIndex belong to this parent
4       4     parent CRC32 (CPathID hash of the parent resource's path)
```

**Entries are sorted ascending by CRC32, treated as unsigned 32-bit.** Confirmed both empirically
(community finding) and now directly in the disassembly, which binary-searches this array by CRC32 on
load — a binary search only works if the array is actually kept sorted, so the engine itself depends on
this invariant, not just tooling built to read the file.

## The children chunk

Immediately after the parents array, three more length-prefixed arrays follow, each with its own
independent u32 count:

```
u32       childHashCount    (M_A)
M_A × u32 childHash[]           — CPathID hash (CRC32) of the child (dependency) resource's path

u32       childTypeIndexCount (M_B, == M_A)
M_B × u8  childTypeIndex[]      — one byte per child: an index into the type table below

u32       typeTableCount    (M_C, independent — much smaller than M_A/M_B in practice)
M_C × u32 typeHash[]            — a small deduplicated table of distinct type CRC32s
```

A parent's children are `childHash[childIndex .. childIndex+childCount)` (and the parallel slice of
`childTypeIndex`) — `childIndex` is a slice start into the two per-child arrays, confirmed directly from
the load loop. Each child's actual type hash is `typeHash[childTypeIndex[i]]`.

**Correction from an earlier pass at this format**: this was first written up, from disassembly alone
before a real sample was available, as *three* parallel per-child arrays (hash, flag byte, second
CRC32), all the same length. Decoding two real shipped files (`entitylibrary_depload.dat`: 433 parents,
1,314 children; `worlds/tmpla/generated/tmpla_depload.dat`: 9,134 parents, 25,838 children) falsified
that: the third array's count-prefix is nowhere near the child count in either file, and every observed
"flag" byte fell inside `[0, thirdArrayCount)` — conclusive for "the third array is a small
deduplicated type-hash table indexed by the second array," not a third per-child field.
(`LoadBinaryFile` also does its own runtime interning of type hashes into an in-memory table with a
per-child byte index after the file is read — a second, unrelated dedup pass over already-parsed data,
structurally similar to the file's own table, which is almost certainly why the first pass conflated
the two.)

The semantic meaning of the resolved `typeHash` itself (e.g. "this dependency is a texture" vs. "a
mesh") is confirmed to be a per-resource-*type* value shared by many children, but not resolved further
— would need either a struct definition recovered from whatever consumes `CResourceDataBase`'s parsed
arrays, or empirical correlation against known resource types across several real files.

## Animations are not like textures

:::tip[Solved: a `.mab` is reachable only through its animation package]
Measured in game. An animation clip at a path present in **no shipped archive** loads and plays
normally, provided it is listed as a `CAnimationResource` child of the `CAnimationPackageResource`
that the weapon's **`sPartName`** names. Registering a clip in a `depload` *is* adding it to an
animation package — they are the same act, which is why the two candidate explanations below
collapsed into one.

Three configurations, one variable each time:

| Clip at an invented path | Result |
|---|---|
| listed in no `depload` | reload never plays; the MOVE state machine enters the reload state and never leaves |
| listed under the **wrong** package (`dart_rifle`) | identical failure |
| listed under the package `sPartName` names (`dragunov`) | **plays normally** |

The middle row is the control that matters: it rules out "any `depload` edit, or the rebuild itself,
fixed it" and pins the cause to package membership.

**The trap is which package.** It is named by the archetype's `sPartName`, *not* by the weapon whose
slot a mod occupies. The VSS Vintorez replaces the Dart Rifle but sets `sPartName = dragunov`, so its
clips belong to the `dragunov` package; a registration under `dart_rifle` is accepted, changes
nothing, and fails silently.

Edit it with [`jackall-cli depload add`](#editing) rather than by hand.
:::

The measurements that led there are kept below, since they are what a similar investigation would
have to repeat.

:::note[How it was found: an unlisted clip did not load]
Measured in game, twice, while repointing the VSS Vintorez's clips (see
[MOVE](./move.md#which-clips-does-a-weapon-play)):

- A `.mab` staged into `patch.dat` at an **invented** path — present in the archive, referenced by
  `movemgr.bin`, listed in no `depload` — **never played**. The MOVE state machine entered the
  reload state and never left it: no reload, no fire, and the weapon stayed dead until the player
  switched away and the state was re-entered from scratch. Switching back left it dead again.
- The **same bytes** at a real shipped path played immediately.

The correlation is exact. The working clip's `CPathID` (`70AEAAE4`,
`…\pneu_dart_model_389\1stge_uppb_reload_+000fw_sp389_i1.mab`) appears in **26 of the 27** shipped
`depload` files, including both campaign worlds. The invented path's (`11641D75`) appears in
**none**.
:::

That was the circumstantial stage: it showed an unlisted clip does not load, not that `depload` was
what stopped it. Registering an invented path and retesting is what settled it — see the box above.

Worth noting alongside it: the one community report of a corrupted `depload` describes
**animations misbehaving** specifically (see [Hand-editing gotcha](#hand-editing-gotcha)), which is
the same mechanism seen from the other side — a parents array the engine can no longer binary-search
loses the package lookup.

Reusing a real path the mod already owns still works and needs no `depload` edit, so it stays the
simpler option when a free slot exists: `jackall-cli move clips --weapon N --shared-only` tells you
which of a replaced weapon's own clip slots no other weapon plays. Registering a new path is what
scales past that, and what a mod adding genuinely new content has to do.

## The type table

`typeHash` is the **CRC32 of the resource's class name, hashed exact-case** — not lowercased, unlike
`CPathID`. Confirmed against `world1_depload.dat`'s 16-entry table; the lowercased form matches
nothing.

The `_depload.xml` form is what gives the names away, twice over: it spells the class out as the
element name, and on many entries it also carries the hash outright as a `crc_Type` attribute.

```xml
<CSoundResource Type="CSoundResource" crc_Type="1676955864" ID="soundbinary\00456a3c.spk"
                crc_ID="1746764574" IsFilename="1" Size="0" nbChildren="2">
```

```xml
<CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\360.mgb.desc" crc_ID="3891716022" version="2">
  <CMagmaUIResource ID="ui\localized\pc\eng\ui\360.mgb" crc_ID="2823636668" />
  <CTextureResource ID="ui\textures\360\360_b.xbt" crc_ID="123948764" />
```

So the XML is the binary's own shape with names restored: a parent element per parent entry, its
children nested inside, `crc_ID` being the `CPathID` the binary stores.

**All sixteen distinct type hashes across every shipped `depload` are identified.** Each name below
hashes to the value observed in a real type table, with none left over:

| `typeHash` | Class | | `typeHash` | Class |
|---|---|---|---|---|
| `BC825377` | `CMaterialResource` | | `B0604725` | `CAnimationResource` |
| `6BD55AFC` | `CTextureResource` | | `44601C12` | `CParticlesSystemParamResource` |
| `6BE083B6` | `CParticlesEmitterParamResource` | | `4CDDA42C` | `CSkeletonResource` |
| `86E8E8BE` | `CGeometryResource` | | `AB064BA6` | `CFaceAnimResource` |
| `63F450D8` | `CSoundResource` | | `59EB7FEF` | `CDominoBoxResource` |
| `1131FDDC` | `CStateMachineResource` | | `1543407D` | `CResourceContainer` |
| `06EA6087` | `CFrankensteinPoseResource` | | `84A30AF0` | `CAnimationPackageResource` |
| `221AD401` | `CMovementResource` | | `3AE88EFD` | `CPhysResource` |

An earlier pass left eight of these unnamed after a guess-list of ~60 plausible `C*Resource` names.
Guessing was the wrong instrument: the names are in the shipped data. Six are stated outright by the
world twins' `crc_Type` attributes, and the rest fall out of matching child hashes against the same
twins — `CFrankensteinPoseResource` and `CParticlesEmitterParamResource` were never going to be
guessed.

`CRealtreeResource`, `CStateMachineBlobResource` and `CResource` appear in the twins as parents but
never as anyone's child, so they never enter a type table.

## Hand-editing gotcha

fdx4061 reported breaking in-game animations by getting the sort order wrong while merging two
`depload.dat` files by hand. Anyone inserting or removing entries must re-sort the whole parents array
by CRC32 afterward (and keep `childIndex`/`childCount` consistent with wherever the corresponding slice
ends up in the children arrays), or the file loads but animations misbehave — not a hard crash, so the
corruption is easy to miss until playtesting.

## Editing

`jackall-cli depload` reads, writes and edits the format. `Encode` re-derives the parents' sort
order, every child slice and the whole type table from the decoded model, so an edit says only what
belongs where and never maintains an index by hand — which is exactly the class of mistake the
[gotcha](#hand-editing-gotcha) describes.

```
jackall-cli depload decode world1_depload.dat          # to XML, paths resolved from the hashlist
jackall-cli depload encode world1_depload.xml
jackall-cli depload validate world1_depload.dat        # sort order, index ceilings, round trip
jackall-cli depload add world1_depload.dat \
    --parent dragunov --child "graphics\...\clip.mab" --type CAnimationResource
```

`--parent` takes an animation package name, a game path, or an eight-digit hex CRC: package names
hash exactly as paths do, so `dragunov` resolves to `E765D26D` on its own.

For a mod, add `--fragment` and stage the result in a layer, which merges into the retail file at
build time instead of shipping a 220 KB binary:

```
mods\worlds\world1\generated\world1_depload.dat\dragunov.3882209901.xml
```

One fragment is one parent and its whole dependency list — about 2 KB.

**The number is the parent's `CPathID` and is what binds; the label in front of it is yours.**
`3882209901.xml`, `dragunov.3882209901.xml` and `anything.3882209901.xml` are the same fragment, so
renaming a staged file cannot orphan the override and two mods spelling a package differently still
land on one entry. This is the scheme a world-sector entity's fragment already uses
(`Guard_12.2058514756624450165.xml`), which is why it needs no special case anywhere. Decimal rather
than hex precisely because that rule keys on a *numeric* tail — and it is how the twins print
`crc_ID` anyway. `depload add --fragment` names the file after whatever you passed to `--parent`, so
you never look a hash up; pass a hash instead and you get the bare form.

A fragment deliberately carries no `childIndex`: that is a whole-file layout detail which shifts
whenever anything earlier in the file changes, so including it would make every fragment churn.

Two mods registering clips under **different** packages compose without either noticing. Under the
**same** package they do not: the merge is line-based, both edits append at the same line, and it
lands as a real conflict. A build resolves it by load order and *reports* it, so the losing clip is
at least named rather than vanishing the way a whole-file override would. Making those merge would
mean canonicalizing children into hash order, and 30% of shipped parents store them in some other
order — not worth trading that fidelity for while the meaning of the order is unknown.

Three properties of all 27 shipped files are what let the encoder rebuild from the model alone, and
JackAll's tests pin each one:

- the child slices are a **gapless, non-overlapping cover** of the child arrays;
- the type table is in **first-use order**, with no unused slot;
- `childIndex` is **not** monotonic in parent order, so block order has to be carried separately.

The format's own ceilings are `childIndex`/`childCount` being `u16` (65,535 children per file;
`world1` uses 29,723) and `childTypeIndex` being `u8` (256 distinct types; 16 are used).

## Unknowns

- **Whether registration is needed for asset types other than animations.** A texture at an
  unlisted path renders fine, and an animation does not load at all; nothing else has been tested.
- **The `_depload.xml` twins are not a byte-exact source for the binary.** In `world1` they agree on
  all 9,718 parents, but 381 parents recur through the nesting and 2,032 localized
  `soundbinary\loc\*.spk` children disagree with the `.dat`, so the XML is a readable sibling rather
  than the file the binary is built from.

Resolved since the first revision: the `_depload.xml` fallback has real samples, and not only the
small `common/ui/localized/*/ui/` ones — **every world ships a full twin** beside its binary
(`world1_depload.xml` is 5.3 MB, and 25 of them exist). They are nested parent/children with the
resource class as the element name, which is what identified the type table.
