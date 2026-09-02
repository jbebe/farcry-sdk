# Mod layout — the closing decision

Settles what an override unit is, so a mod folder stays something a person can read. After this, new
formats get *readers*, not new override-unit shapes.

The goal is **moddability at a granularity you can still navigate**. A mod is a small diff against a
huge game: 163,000 archetypes exist, a mod touches six. Those six should be six small files at a
guessable path — not six 6 MB XMLs, and not six files buried under a tree that only a tool can walk.

Every number below was measured against a retail Steam install (214,097 VFS rows, 46,722 `.fcb`,
69 `.rml`, 54 `.xml`), not carried over from earlier docs.

## Two thresholds decide everything

Granularity is **not** a merge-correctness question. `Diff3` already merges textually distant edits
*inside* one fragment, so splitting finer buys no conflict resolution. Splitting exists to keep the
**shipping payload** small and the folder **navigable** — and those two pull in opposite directions.

So the whole decision reduces to two numbers per container:

> **1. Does it split at all?** Only if the median whole file exceeds **~100 KB**. Below that, the file
> *is* the natural unit — splitting it adds a directory level and saves nobody anything.
>
> **2. How deep?** Take the **coarsest** sub-unit whose median item lands under **~20 KB**, and stop
> there. Never go finer just because a finer key exists.

Rule 2 is the one that keeps the tree readable, and it is the rule most easily got wrong — the
temptation is always to split to the finest key available.

A third constraint gates *whether a split is even expressible*: the child's id must be an
**engine-assigned identity, stable and unique within a defined scope** — a dotted name (→ path), a
numeric engine id, or a type hash that is itself a name hash. If an id needs a synthesized
disambiguator (an occurrence index, a positional counter) the container does not split, because such
an id is not stable across game builds or across a mod's own edits and would silently orphan
overrides.

In practice the size thresholds reject far more than the identity rule does.

## The measurements

*Amplification* = how much dead weight you ship to change one item.

| Container | median file | max file | one item | amplification | verdict |
|---|---|---|---|---|---|
| `EntityLibrary` (fcb) | **6,193 KB** | 6,296 KB | 1.3 KB | **4,904×** | split per archetype |
| `NewPartLib` (rml) | **4,020 KB** | 5,980 KB | 16 KB | **250×** | **split per `PartSys`** |
| `stringtable` (rml) → section | **978 KB** | 1,290 KB | 1.7 KB | **565×** | **split per section** |
| `stringtable` (rml) → string | 978 KB | 1,290 KB | 73 B | 13,400× | *too fine — rejected* |
| resource manifests (xml) | 2,073 KB | 5,314 KB | 321 B | 6,459× | generated — excluded |
| `mapsdata` (fcb) | 24 KB | 524 KB | 35 KB | **1×** | no split |
| `sectorsDep` (fcb) | 4 KB | 293 KB | 9.7 KB | **<1×** | no split |
| `ActionMap` (xml) | 12 KB | 43 KB | 185 B | 67× | no split |
| `Sector` (fcb) | 2.3 KB | 12 KB | 140 B | 16× | no split |
| Barks (fcb) | **1.3 KB** | 50 KB | 359 B | 4× | no split |
| `WorldSector` (fcb) | **4.3 KB** | 364 KB | 1.1 KB | 4× | splits today — see below |

## What to add: exactly two

**`stringtable` → one file per `<section>`.** 978 KB down to 1.7 KB, and this is the single highest-value
gap in the tool: 11,394 strings live in one file, so every retranslation, weapon rename or dialogue
tweak is a whole-file override today. Key is `section@name`.

Stop at the section — do **not** go per-string, even though `string@enum` is a perfect key (unique
within every section, 0 duplicates measured). A section is already 1.7 KB, so per-string saves 1.6 KB
of payload while turning a retranslation mod from 61 readable files into **11,394**. That is Rule 2
doing its job.

**`NewPartLib` → one file per `PartSys`.** 4 MB down to 16 KB. `@Name` is dotted and unique
(`destructibility.wood.wood_chair`), so it maps onto a path exactly like `hidName` does. A particle mod
touches a handful of effects; today it ships 4 MB to change one.

That is the whole addition. Two recognisers.

## What not to add, and why

- **`mapsdata` and `sectorsDep`** — a natural key *does* exist (each child's type hash resolves to an
  uppercase level name, `W2_A_1` … `W2_E_5`, confirmed for all 25 children of a world). This corrects
  [fcb-deep-fragments.md](fcb-deep-fragments.md), which lists both as unkeyable. But the key is not
  worth using: one level is ~35 KB against a 24 KB median file — **amplification 1×**. Splitting would
  make fragments *larger* than most of the files they come from. Only the 524 KB outlier would benefit,
  which is not enough to spend a directory level on.
- **`ActionMap`** — 67× amplification, but the whole file is only 12 KB. Under the line, so the file is
  the unit. Input rebinding is a real mod category and a 12 KB diff serves it fine.
- **Barks** — 1.3 KB median file. Nothing to gain. (They also fail the identity rule: the composite
  `(BarkEventTag, SourceActorTag, TargetActorTag)` is unique in 822 of 863 containers but **collides in
  41**, so an id would need an occurrence index. Two independent reasons to leave them whole.)
- **Resource manifests** (`CSoundResource` / `CGeometryResource` / `CMagmaConfigUIResource`, 48,110
  items across 42 files) — 6,459× amplification and a unique `@ID`, so they pass both size and identity
  tests. Excluded anyway because they are **generated dependency manifests**, the XML sibling of
  depload. A mod should never hand-ship one; they are rebuilt from the asset set.
- **`Sector`, `.mgb`, `.spk`, `.sdat`, `.xbt`, `.xbg`, `.hkx`** — small files, fixed-shape records, or
  asset payloads rather than collections of independently authored items.

**`WorldSector` is the one exception to the payload rule**, and it is worth being honest about: at a
4.3 KB median file and 4× amplification it would not pass Rule 1 today. It splits because whole-file
overrides are *last-wins* while fragment overrides get Diff3-merged, so per-entity fragments are what
let two mods edit different entities in one sector. The 364 KB tail also justifies it. It stays — but it
is the precedent for "conflict merge", not for "payload", and nothing else should cite it as one.

## What a mod folder actually looks like

The point of all of the above. A mod that rebalances two weapons, retitles a menu, and tweaks one fire
effect is **five files**:

```
MyMod/
  worlds/worlds/world1/entitylibrary.fcb/
    weapon/Ranged/AK47.xml                    1.3 KB
    weapon/Ranged/Dragunov.xml                1.3 KB
  misc/oasisstrings.rml/
    menu_main.xml                             1.7 KB
  misc/particles.rml/
    fire_propagation.fire_propagation.fire_grass.xml   16 KB
  worlds/worlds/world1/worldsector42.data.fcb/
    Guard_12.2058514.xml                      1.1 KB
```

~21 KB total, every path readable, every filename saying what it is. Nothing else from the 46,722
containers or 163,000 archetypes exists on disk — that is the property worth protecting.

## The one mechanism change

The addressing layer is **already format-agnostic** and needs no change:

- `IModLayer.FragmentOverrides` is `Dictionary<uint containerHash, ...>` of
  `FragmentOverride(string FragmentId, uint EntryHash)` — the id is a plain string.
- `GameVfs._fragmentOverrides` is keyed the same way.
- The on-disk convention `<container>.<ext>\<fragmentId>` says nothing about FCB.

Only the **splicing** layer is FCB-coupled: `FcbFragments`, `FcbAssembler.Apply` and
`FragmentMerge.Resolve` all speak `FcbObject`. So the change is to lift a container-splitter interface
— *recognise / list / extract / apply* — out of `FcbFragments`, with two implementations: the existing
FCB one, and an `XElement` one covering `.rml` (via `RmlDocument`) and plain `.xml`.

That the on-disk convention needed no change at all to absorb both new formats is the evidence it was
the right convention.

Two consequences to handle deliberately:

- **`OasisStringTable` lives in `JackAll.App`**, but splitters belong in `JackAll.Core` alongside the mod
  pipeline. Moving it down is a prerequisite for the `stringtable` recogniser, and it is also what
  [Track 5](/todos/roadmap) needs for a localization editor. One move, two payoffs.
- **Particle libraries and string tables each ship twice** — compiled (`.rml`) and plain-text
  (24 `<NewPartLib>` `.bin` totalling 164 MB, 10 `<stringtable>` `.bin` at 11.4 MB). **Target the
  `.rml`; the other form is dead.** Verified against both shipped DLLs rather than inferred:

  | | evidence |
  |---|---|
  | Strings | The only oasisstrings literal in either build is `%s\%s\oasisstrings.rml`. **No `.xml` variant exists anywhere in either DLL.** Loaded by `CStringTableMgr::LoadStringTable` — Steam (`fc2_103_uplay`) `0x4D29A0`, GOG (`fc2_103_retail`) `0x4C5210`, both 557 B, building `LANGUAGES/%s/%s`. Archive agrees: the `.rml` is in `patch.dat` and overrides `common.dat` (956,291 B), while the `.xml` is `common.dat`-only and never patched (1,132,339 B) — a stale pre-patch leftover. |
  | Particles | `%s%s_deploadnewparticles.rml` in `CFCXEditorDocument::ExportWorld` (writer) and `_deploadnewparticles.rml` in `FUN_1065B5B0` (loader, 562 B, unnamed). Confirmed by hash: `worlds\world1\generated\world1_deploadnewparticles.rml` = `0xF3C53A6F`. |

  The general rule for telling the variants apart: **whichever form `patch.dat` ships is the live one.**
  `mod lint` should reject a layer staging fragments against the other.

## Cost

| | rows |
|---|---|
| Today, fragments off | 214,097 |
| Today, fragments on | ~1,090,000 |
| `stringtable` per section | +671 |
| `NewPartLib` per `PartSys` | +7,008 |

Under +1%. And in a *mod folder* the cost is zero — only changed items exist on disk.

## Closed questions

Settled. Reopening one needs a new measurement, not a new opinion.

- **Splitting to the finest available key.** No. Rule 2. This is what caps strings at section level.
- **Splitting containers under ~100 KB.** No. Rule 1. This is what rejects `mapsdata`, `sectorsDep`,
  `ActionMap`, barks and `Sector`.
- **Positional or synthesized ids.** No — not stable, would orphan overrides.
- **Splitting generated manifests.** ~~No, unless a pipeline ever needs to inject a resource by
  hand.~~ **Reopened, and `depload.dat` now splits** — the escape clause fired. See
  [below](#reopened-depload-splits).
- **Deeper than one key level past the container.** No. Mission-layer granularity inside a sector was
  already rejected (97% of entities sit under `main`); the same logic holds everywhere else.
- **Fragment overrides reducing archive size.** They do not and never will: `Apply` re-encodes the whole
  container and drops shared-data backreferences (measured 18,525 → 32,256 B on one sector). Orthogonal
  change, separate pass.
- **Per-format bespoke layouts.** No. Everything is `<container>.<ext>\<fragmentId>`.

## Reopened: `depload` splits

This section excluded resource manifests as "generated dependency manifests… A mod should never
hand-ship one; they are rebuilt from the asset set." That reasoning assumed a pipeline is available
to rebuild them. **For a mod there is none**, and an animation clip at a new path does not load
until it is registered in a `depload` — measured in game, see
[depload](/docs/file-formats/depload). So the escape clause the Closed questions list already
wrote — *"unless a pipeline ever needs to inject a resource by hand"* — is met, and reopening it
needs a measurement rather than an opinion:

| | measured |
|---|---|
| median `depload.dat` | **102 KB** (min 10 KB, max 223 KB) — clears Rule 1's ~100 KB, but only just |
| one item (a parent and its dependency list) | median **331 B**, p90 784 B, p99 8.4 KB, max 43.7 KB |
| amplification | **315×** |
| identity | the parent's `crc_ID`, a `CPathID` — the "type hash that is itself a name hash" the identity rule already admits |
| depth | stop at the **parent**. Per-child would be one dependency per file, which is Rule 2's whole point |

All three gates pass, the 2 KB margin on Rule 1 included, and the on-disk convention needed no change
at all — `<container>.<ext>\<fragmentId>` absorbed a third format exactly as it absorbed the second.

The id scheme needed no invention either: `<label>.<crc32 decimal>.xml` is the **same cosmetic-name /
authoritative-number shape a placed entity already uses**, so `FcbFragments.IdComparer` collapses the
label with no special case. That property is load-bearing rather than cosmetic here, because the two
sides do not know the same names: JackAll labels a resource from the hashlist, which covers 7,543 of
world1's 9,718 and contains **no animation packages**, while a mod author writing `dragunov` does
know one. Binding on the number lets each label the entry however it can and still land on one entry.
Addressing by the *path* instead was tried and reverted: it reads better, but a nested label
canonicalizes with its directory kept, so a listed row and a staged file stop matching and the same
resource shows up twice. Two details are specific to depload: the label must be a flat leaf, and a
fragment omits `childIndex`, a whole-file layout value that shifts whenever anything earlier changes
and would otherwise make every fragment churn.

**The splitter interface named in [The one mechanism change](#the-one-mechanism-change) is now
built**, as `IContainerSplitter`/`IContainerTree` with `FcbContainerSplitter` and
`DepLoadContainerSplitter`. `stringtable` and `NewPartLib` remain the two additions this document
called for; they now only need an implementation each, not a refactor.

## Correction to fcb-deep-fragments.md

It lists `mapsdata` and `sectorsdep` among containers whose "children carry no name field, so they
cannot decompose." Both are in fact keyed, by a type hash that resolves to an uppercase level name —
but the conclusion (leave them alone) is right, for the size reason above rather than the key reason.

`managers` and `omnis` are identified: `CFCXEditorDocument::ExportWorld` writes `%s%s.managers.fcb`
and `%s%s.omnis.fcb` as per-world editor exports, siblings of `%s%s_depload.dat` and
`%s%s_deploadnewparticles.rml`. Neither appeared as a distinct root shape in this survey, so neither
is a live aggregate in the shipped archives.
