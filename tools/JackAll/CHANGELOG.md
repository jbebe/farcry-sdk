# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **A legacy import no longer stages a mod's rounding as an edit** — the editors these mods were
  built with rewrite every float they read, so a mod that moved one crate arrived claiming to
  override every entity around it, at values differing from vanilla in the last bit or two. The
  importer now compares floats with a precision interval of 8 units in the last place. Measured
  against Scubrah's Patch, whose rounding reaches 7 ULP while its real edits start at 69,632, so the
  two do not overlap. Staged fragments drop from **24,924 to 7,434** for Scubrah's Patch, 12,734 to
  885 for Realism Plus Redux and 12,136 to 501 for Functional Outposts, with every whole-file
  fallback, mission layer, deletion and real edit unchanged. A fragment that *is* staged, for a real
  edit, also has its remaining floats put back to vanilla's own values, so it carries the edit and
  nothing else — **1,653 values across 658 fragments** for Scubrah's Patch. Values are matched by the
  name they carry rather than by position, so a component the mod added does not shield the untouched
  ones beside it. Whole numbers are still compared exactly, since a large id can round to the same
  float as its neighbour.
- **`<world>.game.xml` is overridden one mission — or one section — at a time** — the file that
  declares which mission layers exist and when each is on, so every mod adding a mission or an
  outpost had to override it whole and last-wins against every other such mod. It now splits per
  `<Mission>` *and* per top-level section beside them (`_environment.xml`, `_grids.xml`,
  `_layers.xml`, named after the element so a section nobody has seen yet still gets one). That
  second half matters as much as the first: Scubrah's Patch raises the shadow radius and view
  distance in `Environment`, and one such edit used to cost it the whole descriptor. Both of its
  world descriptors now split — **275 KB of fragments in place of a 612 KB whole-file override for
  world2**, with the environment edit isolated in a 3.8 KB fragment — taking its whole-file
  fallbacks from 9 to 7. It also stops a whole-file override from marking all 1,515 of a
  descriptor's missions as modded when the mod changed a handful. The flat
  `<MissionLayers>` index rebuilt from the missions rather than maintained by hand. Realism Plus
  Redux's world1 edit becomes **32 mission fragments totalling 12,758 bytes in place of a 365,187
  byte whole-file override**, so two mods adding different outposts finally merge. The plain-text
  multiplayer template keeps its whole-file override, since re-serializing it could not be
  round-tripped safely.
- **An entity can be deleted from a sector without claiming the sector** — `_layers.xml` gained
  `<delete id="…"/>`. Removing one crate used to mean a whole-file override, which outranked every
  other mod touching any of that sector's other entities. Deletion is still exclusive, because two
  mods disagreeing about whether something exists genuinely disagree, but it is now exclusive over
  **one entity instead of one file**. Where another enabled mod edits an entity you deleted, the
  entity is kept and the collision is reported rather than silently resolved. With this, no world
  sector in either of the two largest community mods falls back to a whole-file override.
- **A world sector's mission layers are overridable** — an entity is spawned from the layer it sits
  in, not from the `CMissionComponent` on it, so an outpost mod that moves guards into a mission
  layer of its own could not be expressed per fragment and landed as a whole-file override. A sector
  now has one more override unit, `_layers.xml`, saying which layer its entities belong to; a mod
  states only what it moved, and two mods re-filing different entities of one sector merge instead of
  fighting. Importing Realism Plus Redux takes its whole-file fallbacks from **96 to 8** and its
  staged fragments from 669 to **12,542**.
- **A fragment shows what it lives in** — the Files tab names the mission layer or library group a
  fragment sits in, which its id deliberately does not record. An entity whose mission component
  claims a different layer than the sector nests it under is flagged in the details pane and in the
  editor: the game spawns it from where it sits, so that edit changes nothing on its own.
- **A whole-file fallback says so** — `mod import-legacy` used to coarsen an `.fcb` to a whole-file
  override in silence. It now reports every one, in the app, the CLI and `--json`, and names the
  reason - down to which entities moved into which mission layer.
- **Legacy import splits every container, not just `.fcb`** — `mod import-legacy` used to diff a
  legacy patch fragment-by-fragment only for entity libraries and world sectors; a mod that changed
  `movemgr.bin` or a `*_depload.dat` landed as a whole-file override, last-wins and silent. The
  fine-grained path is now format-agnostic over `IContainerSplitter`, so every splitting container
  imports as the fragments that actually differ. A MOVE graph whose change no fragment can carry is
  left out with a warning rather than overridden whole — reported in `--json`, in the CLI's output
  and in the app — while `.fcb` and `depload` keep their whole-file fallback, which the community's
  largest legacy mods still need for ~100 world sectors apiece.
- **`oasisstrings.rml` is overridden one string at a time** — a localization mod ships one
  `oasisstrings.fragment.xml` per language stating only the strings it changes, instead of
  overriding all 11,394. Two mods that rename different weapons never meet, even inside one section;
  only two mods rewriting the *same* string conflict, and that is reported rather than swallowed.
  The VSS Vintorez ships **1,271 bytes in place of 946 KB**. **Breaking:** a whole-file
  `oasisstrings.rml` override is now refused, since it is last-wins against every other localization
  mod and silent about it. `jackall-cli rml fragments` writes the patch document, and
  `mod import-legacy` converts one automatically.
- **Unused files, in the Files tab** — "Hide unused game files" drops every base-game file the
  engine can never open and prunes the folders left holding nothing else; the rows that remain
  visible when it is off are italicised. Selecting one explains in the details pane *why* it is
  dead, and staging an edit over one asks first — once per file per session, from whichever button
  or editor tab you save with. Your own edits are never hidden and never silently blocked: the
  warning explains that the edit will deploy normally and still do nothing in game.
- **`xref reach` CLI** — classifies every file in an install as `used`, `used-sp-only`,
  `used-mp-only`, `unused` or `unknown` by walking the reference graph out from the roots
  `Dunia.dll` itself names (`assets/engine-roots.tsv`: 153 hardcoded paths and 49 filename
  templates, each with its source address). The headline output is the decoy table — files that are
  dead but shaped like they matter, such as the 24 six-megabyte `entitylibrary_full.fcb` copies and
  the `_depload.xml` twins. `unknown` is a real third state: a file nothing *could* have referenced
  in a form the tools read is never called dead. Verdicts for the whole retail corpus ship as
  `assets/fc2.unused.tsv`; the method is documented under Asset reachability in the docs site.
- **`.xbt`, MOVE and `.rtx` reference extractors** — an `.xbt` header's `_mip0` companion, a MOVE
  graph's clip hashes, and an `.rtx` species' material slots (rewritten from the authoring `.mlm` to
  the `.xbm` that ships) now reach the reference index. Each closed a source of files that looked
  unreferenced only because nothing parsed the format naming them.
- **Move tab** — the animation graph the engine picks clips with (`movemgr.bin`, `dlc1.bin`),
  browsable as the ownership tree it reads back as. Criteria are labelled with the channel and enum
  value they test where a named twin sits beside the graph, so a rule reads as
  `EquippedWeapon == SawedOffShotgun` rather than `17 == 42`. Read-only.
- **`move` CLI** — `decode`, `encode`, `verify`, `clips`, `validate` and `hash` for MOVE graphs.
  The XML is an interchange format using the engine's own field names, with clip and state hashes
  resolved to game paths by default; `verify` reports whether a graph reads back to the bytes it
  came from, which also checks the pointer graph because every back-reference is renumbered from
  object identity rather than replayed.
- **`move clips --weapon N`** — the clips an EquippedWeapon index plays, scoped by criterion rather
  than by folder, and flagging the ones another weapon plays too. Repointing a shared clip changes
  how that other weapon animates: the Dart Rifle's own folder holds a draw clip the MGL-140 plays,
  and its jam cycle is borrowed from the AK-47.
- **`move repoint --weapon N --map pairs.tsv`** — retargets the clips one weapon plays. Only sites
  that weapon governs are rewritten, so a clip it shares keeps playing for the other weapon. A
  mapped clip also reachable from a site no weapon governs makes the repoint *incomplete* — the
  weapon still plays the original through that path — and the command says so and exits non-zero
  rather than reporting success.
- **`move validate`** — clip references that no known game path hashes to, which catches a mistyped
  repoint map before it ships a graph that parses and plays nothing.
- **`depload` CLI** — `decode`, `encode`, `add` and `validate` for the per-world dependency index.
  Writing it is what lets a mod ship content at a path the game never had: an animation clip only
  loads if it is listed under the `CAnimationPackageResource` the weapon's `sPartName` names, which
  is now measured in game rather than inferred. `add` re-derives the parents' sort order, every
  child slice and the type table, so the mistake that misbehaves animations without crashing cannot
  be made by hand.
- **`depload.dat` splits into fragments** — stage one resource's dependency list at
  `mods\worlds\world1\generated\world1_depload.dat\dragunov.3882209901.xml` (about 2 KB) — the number
  binds and the label is the author's, as with a world-sector entity — and the build merges it
  into the retail file, instead of shipping a 220 KB binary. `depload add --fragment` writes one.
  Two mods registering clips under different animation packages then compose; under the same
  package the build reports the collision instead of silently dropping one mod's clip.

### Changed
- **Xrefs answer for a depload resource, not just the file it sits in.** Selecting one resource's
  entry shows what *it* pulls in under References, and what lists *it* under Referenced by — a clip
  now names the animation package that plays it. Previously a fragment row reported no references at
  all, because its VFS key is synthetic and the index never saw it, which was a poor answer for a row
  made of nothing but references.
- **`depload.dat` now browses like a splitting `.fcb`.** It expands in the file tree into one row per
  resource, under the id a mod stages it at (`dragunov.3882209901.xml`) — the same
  entries a mod stages, so a row can be diffed against vanilla and mirrored straight into the
  workspace as a fragment override. This replaces a synthetic row per parent *and* per child, over
  half a million of them, each pathed by its target so the explorer grew a fake folder for every path
  segment of every dependency; xref already indexed those edges properly.
- **Container splitting is no longer `.fcb`-only.** The fragment machinery now runs through an
  `IContainerSplitter`, so a format supplies *recognise / open / extract / apply* and inherits the
  three-way merge, load-order folding and `_hash\` addressing unchanged. `.fcb` and `depload.dat`
  are the two implementations; `stringtable` and `NewPartLib` need only an implementation each.

## [1.0.0] - 2026-08-23

First release.

### Added
- **Mods tab** — an ordered stack of mod zips, later winning, with your own edits in a `workspace\`
  layer pinned last. A mod zip is just a tree of relative paths, so community mods drop straight in.
- **Files tab** — all 13 game archives merged into one browsable filesystem, as the engine resolves
  it, with anything a mod supplies highlighted and diffable against vanilla.
- **Safe, reproducible builds** — every build regenerates `patch.dat` from a one-time
  `patch.dat.vanilla` backup, never from what is on disk, and never writes the base archives. The
  result loads in the stock engine: no DLL, nothing running in the game process.
- **Legacy mod import** — converts an old-style whole-archive `patch.dat`/`patch.fat` mod by diffing
  it against true vanilla, staging only what it actually changed.
- **Cross-mod merging** — two mods editing different parts of the same `.fcb` entity are merged
  three-way rather than one silently overwriting the other; a genuine collision is an error.
- **`.fcb` editor** — entity libraries split one file per entity, decoded to a component/field tree
  rather than hex.
- **Format editors and viewers** for `.rml`, `.sbao` (audio import/export/preview), `.xbt`, `.xbm`,
  `.xbg` and `.rtx` (3D preview), `.sdat`, `.spk` and `.mgb`, plus a text/XML/Lua editor.
- **`.fc2model` export and apply** — collects a model with its materials, textures, rig and
  animations into one file a 3D editor opens, and stages back what the editor changed. The Blender
  add-on that reads it ships beside JackAll.
- **Map tab** — the layers an FC2 world is built from, over a 3D viewport of its terrain. Read-only.
- **Saves tab** — `.sav` metadata, and hand-editing of a save's `PersistenceDB` tree.
- **CLI** — the mod pipeline (`status`, `inspect`, `import-legacy`, `build`, `restore`) and every
  format converter, each with `--json`. This is what the Vortex extension drives.
- **Files nobody has a name for are still moddable** — about 54,000 of the game's 214,000 entries.
  They are sniffed for a type, listed under `_unknown\`, and edited as `_hash\<crc32>.<ext>`.
