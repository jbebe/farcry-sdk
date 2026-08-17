# Deep FCB fragments — nested, per-entity and per-archetype override units

Plan for a separate session. Self-contained: everything needed to start is below, including the
measurements the decisions rest on.

## Context

JackAll can already override *one child* of a splitting `.fcb` instead of replacing the whole
container — a "fragment override". Today that only works one level deep and only for one recognised
shape, so:

- **`worldsector*.data.fcb` has no fragment support at all.** Its root is `WorldSector`
  (`0xC1CB6D9A`), which no recogniser matches, so editing a single placed entity stages a
  **whole-file** override. Two mods editing *different entities in the same sector* conflict at file
  level with no merge attempted. This is the gap worth closing.
- **Entity libraries split only into their ~37 `NN_Name.xml` groups.** Editing one weapon stages a
  whole group. `Diff3` already 3-way merges textually distant edits inside a fragment, so this mostly
  costs robustness rather than capability.

Goal: fragment ids become **paths**, so an override unit can be one archetype or one placed entity.

```
entitylibrary.fcb\vehicle\Land\DLC_Vehicle1_DLC1.xml
worldsector17.data.fcb\StaticObject_201.2058514756624450165.xml
```

## What this does NOT fix

**It will not reduce file size.** `FcbAssembler.Apply` does `Deserialize → replace child →
FcbDocument.Serialize(root)` — it re-encodes the *whole* container, and `Serialize` always writes the
fully expanded form, dropping the shared-data backreferences the shipped files use. A real sector
measured **18,525 → 32,256 bytes** through that path. Per-fragment overrides inflate a container
exactly as much as whole-file ones do.

If size matters, that is a **separate** change worth its own pass, and it helps every override path:
either teach `FcbDocument.Serialize` to emit backreferences, or have `Apply` copy untouched children's
original bytes verbatim using the offsets `FcbDocument.DeserializeWithChildSizes` already returns.

Also out of scope: containers with no natural key for their children (`mapsdata`, `managers`,
`omnis`, `sectorsdep`). Their children carry no name field, so they cannot decompose. This is a
per-shape recogniser mechanism, not a general one.

## Starting state — read this first

**The work this builds on may still be uncommitted.** Check for these; if absent, they are in the
reflog or need redoing:

- `tools/JackAll/src/JackAll.Tools/World/ArchetypeIndex.cs` — override-chain resolution, plus
  `SplitForDisplay`, `DiscoverWorlds`, `DiscoverDlcLibraries`
- `tools/JackAll/src/JackAll.Tools/World/ArchetypeLint.cs`
- `tools/JackAll/src/JackAll.App/Library/` — the Library tab
- `tools/JackAll/src/JackAll.App/MapEditor/EntityTreeNode.cs` — the Map tab entity tree
- `tools/JackAll/src/JackAll.App/TreeNodeBase.cs`
- `MainViewModel.StageFragmentEdits` / `StageContainerEdits` in `MainViewModel.Vfs.cs`
- `MainWindow.OpenSectorEditorTab` in `MainWindow.EditorTabs.cs`

Baseline: `dotnet test tools/JackAll/src/JackAll.Tests/JackAll.Tests.csproj` → **705 pass, 2 fail**.
The two failures are `MgbXmlTests.The_fcse_page_package_builds_from_its_xml_in_the_shape_fcse_expects`
and are pre-existing, unrelated to FCB.

## Facts this plan rests on (all verified)

**The current mechanism**

- `FcbXml.TryGetFragmentIds` matches exactly one shape: root `TypeHash == 0xBCDD10B4`
  (`EntityLibrary`), no root values, and every child `0xE0BDB3DB` (`EntityLibraryGroup`). Ids are
  assigned as `NN_<Name>.xml` over `root.Children`, in order.
- `FcbAssembler.Apply(baseFcb, fragmentXmlById)` matches ids against `root.Children[i]` **by index**,
  replaces matches, and appends unmatched ids as new children in ordinal order (so a build is
  deterministic). Returns the input untouched when there is nothing to splice.
- A staged fragment lives at `<container path>.fcb\<fragmentId>` for a named container, or
  `_hash\<container hash:x8>.fcb\<fragmentId>` for an unnamed one — see `MainViewModel.Replace` and
  `ModPathHashing.Resolve`.
- `IModLayer.FragmentOverrides` is `IReadOnlyDictionary<uint /*containerHash*/,
  IReadOnlyList<FragmentOverride>>`, `FragmentOverride(string FragmentId, uint EntryHash)`. The id is
  already a string, so nesting is representable without a model change.
- `GameVfs.Read` splices via `_fragmentOverrides`, built by `FragmentMerge.BuildOverrideIndex` from the
  enabled layers in `Rebuild` — independent of whether fragment *rows* were enumerated
  (`includeFragments`). `GameCache` persists decoded fragment structure per container.

**Measured scale** (retail Steam install, warm cache)

| | today | if deep-fragmented |
|---|---|---|
| VFS rows | 726,179 | ~1,090,000 (+50%) |
| `.fcb` containers | 45,858 | unchanged |
| fragment rows | **1,728** | **~364,000** (210×) |
| containers that actually split | **51** | 51 + 17,945 |

- 51 entity libraries → **163,203** archetype rows.
- 17,945 `worldsectors\*.data.fcb` containers × **11.2** entities each → **~201,000** entity rows.
- `GameVfs.Load` with fragments already takes **7.3 s** warm.
- Only 51 of 45,858 containers split today, so the existing pass is nearly all shape-checking with a
  tiny payload. Deep fragmenting concentrates real work in ~18,000 containers.

**Shape facts**

- Worldsector: root `WorldSector` (`0xC1CB6D9A`) → ~33 `MissionLayer` children → `Entity` nodes.
  **97% of entities sit under the single `main` layer** (87,521 of 90,605 in world1), so splitting per
  mission layer would produce one giant fragment and is not worth doing.
- Entity library: root → group → `EntityPrototype` → `Entity`, and prototypes sit **exactly two
  levels** below the root in every shipped library checked (asserted by
  `ArchetypeIndexTests.Prototypes_sit_exactly_two_levels_below_the_library_root`).
- Relevant hashes: `Entity` `0x0984415E`, `hidName` `0xB9295CC7`, `Name` `0xFE11D138`,
  `disEntityId` via `WorldHashes.DisEntityId`. `EntityLibrary` `0xBCDD10B4`,
  `EntityLibraryGroup` `0xE0BDB3DB`.

## Identity keys — decide this before writing code

The id is what a mod stores on disk, so it must be stable across game versions and across a mod's
lifetime.

**Archetypes: key on `hidName`.** It is the engine's own map key
(`CEntityLibraryManager::BuildArchetypesMap`), unique, case-insensitive, and already dotted — so it
maps straight onto a path. `entitylibrary.fcb\vehicle\Land\DLC_Vehicle1_DLC1.xml` is exactly right.

**Entities: key on `disEntityId`, not the name.** `hidName` is *not* unique in a sector and two
mission layers can carry the same name, so a name-only id is ambiguous. `disEntityId` is documented as
the stable identity mission scripts reference entities by.

To keep paths readable, use `<name>.<disEntityId>.xml` and treat **the trailing numeric id as
authoritative, the name prefix as cosmetic**. Normalise to the numeric id when building the override
index, so a mod that renamed an entity still matches, and two spellings of the same entity cannot
produce two competing overrides.

**Do not put the mission layer in the id.** Moving an entity between layers would silently change its
identity and orphan the override.

## Work

### 1. Depth-aware fragment ids — the enabler

Everything else is a recogniser on top of this.

- Generalise `FcbXml`'s single hardcoded shape check into a small set of **recognisers**, each
  answering: does this root match, and what are its `(fragmentId, node)` pairs? Return ids as
  `\`-separated paths.
- `FcbXml.ListFragmentIds` / `ListFragmentsWithSize` / `ExtractFragment` keep their signatures; only
  the id space widens. `ListFragmentsWithSize` needs per-fragment byte sizes at depth — it currently
  pairs ids positionally with `DeserializeWithChildSizes`'s top-level sizes, which no longer lines up.
  Either compute expanded sizes for nested nodes or report 0 and drop the size column for them; decide
  and say which in the code.
- `FcbAssembler.Apply`: resolve an id to a node **by path** rather than by child index, replace in
  place, and keep the append-unmatched-in-ordinal-order rule for new content. Appending at depth needs
  a defined parent — for a new entity, the `main` mission layer; make that explicit, not implicit.
- Check the on-disk round trip: a nested id becomes nested directories under the container folder, and
  `NameHash.Normalize` lowercases and collapses separators. Confirm
  `ModPathHashing.Resolve` reconstructs a nested id from a staged path, and that
  `ModLayerInspector` still classifies such a tree as a layer.

### 2. Worldsector recogniser — do this first

Highest payoff: it is a new capability, not an improvement to an existing one.

- Recognise root `WorldSector`, walk `MissionLayer` children, emit one fragment per `Entity`.
- Id per the rule above. Skip any entity with no `disEntityId` rather than inventing one.
- Once this lands, `MainWindow.OpenSectorEditorTab` should open the **entity's** fragment instead of
  the whole sector, and can go back to `StageFragmentEdits`. Rename the Map tab button accordingly —
  it is currently deliberately labelled "Open sector in XML editor" because it opens the whole file.

### 3. Per-archetype library recogniser

- Recognise the existing entity-library shape but descend to `EntityPrototype` → `Entity`, emitting one
  fragment per archetype keyed on `hidName`.
- **This changes existing ids**, so decide the migration: keep the group-level ids working as an alias,
  or convert layers on load. `mod import-legacy` also diffs entity libraries fragment-by-fragment, so
  its output granularity changes — check `LegacyPatchImporterTests` still holds.
- The Library tab then opens just the archetype, and `ArchetypeLint.DeadDeclarationsIn` becomes exactly
  per-archetype instead of per-group.

### 4. Keep the rows lazy

364,000 fragment rows must not be materialised on every load.

- `GameVfs.Load(includeFragments: false)` and background `LoadFragments` already exist for this
  pressure — make sure the deep path uses them and that `GameCache`'s per-container structure section
  still round-trips nested ids.
- Measure `GameVfs.Load` cold and warm before/after; if the warm figure moves far past 7.3 s, decode
  worldsectors on demand (per container, on first access) rather than in the bulk pass.

## Risks

- **`PatchBuilder` determinism.** Two builds from the same layers must be byte-identical.
  `Apply`'s ordinal append order is what guarantees that today; nested appends need the same property.
- **Id stability across game builds.** `disEntityId` and `hidName` are engine identities, so they
  should hold, but a mod authored against one install and applied to another (GOG vs Steam vs the
  press build) should be spot-checked.
- **Index growth** (+50% rows) hits the Files tab grid, `ReferenceIndexer`, and `GameCache` size.
- **No size win** — see the non-goals; do not let this be sold as one.

## Verification

1. **Unit, on real fixtures.** `tools/JackAll/src/JackAll.Tests/Fixtures/Fcb/` has
   `worlds_entitylibrary.fcb` (650 archetypes), `patch_entitylibrarypatchoverride.fcb` (915),
   `dlc1_entitylibrary.fcb` (42), `dlc_jungle_entitylibrary.fcb`. Follow the existing convention:
   `[Trait("Category","RequiresFixture")]` plus a `The_fixture_files_were_actually_found` guard fact.
   - per-archetype ids are unique within a container, and count matches the archetype count
   - `Apply` with one nested override changes only that archetype: re-parse and assert every other
     archetype is byte-identical to vanilla
   - `Apply` with no overrides still returns the input instance (`Assert.Same`) — existing behaviour
   - a nested id survives staged-path → id round-tripping through `ModPathHashing`
2. **No worldsector fixture exists.** Extract one from
   `tools/third-party/Far Cry 2 Dedicated Server (debug)/data_linux/Worlds/worlds.fat`, or via
   `jackall archive extract worlds.fat --names --filter worldsector`. A ~10 KB sector (median) with
   ~11 entities is a good fixture; commit it alongside the others.
3. **Regression.** `FcbDocumentTests`, `FcbXmlTests`, `FcbValueCodecTests` (re-encodes every value in
   every fixture byte-for-byte), `FcbAssemblerTests`, `GameVfsFragmentOverrideTests`,
   `PatchBuilderTests`, `ModLayerInspectorTests`, `LegacyPatchImporterTests` must stay green — they sit
   directly under this change. Expect the same 2 `MgbXml` baseline failures and nothing else.
4. **Merge behaviour, the actual point.** Two layers each overriding a *different* entity in the same
   sector must both survive a build. Today that conflicts; assert it merges.
5. **End to end.** `jackall mod build` twice from the same layers → byte-identical patch.dat. Then load
   the game and confirm an edited entity and an edited archetype both take effect.
6. **Docs.** Update `docs/docs/file-formats/fcb.md` and `docs/docs/modding/vortex.md` (which documents
   the `<container>.fcb\NN_Name.xml` staged layout) to describe the new id space. `cd docs && npm run
   build` is strict; note that `modding/guide/patrols` has pre-existing broken anchors unrelated to this.
