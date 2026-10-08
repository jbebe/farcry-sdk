# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Two mods adding to one `.fcb` entity merge** — a fragment merges as a tree, fields paired by
  name and child objects by type, so two mods that each add a component or an FCSE entity-data key
  to one archetype both keep theirs. A list of same-typed records merges record by record, each
  matched to the original by content, so two mods adding event links to one entity both keep theirs.
- An FCSE entity-data key merges as one value, and a fragment listing one key twice is refused.
- **Every fragment merges as a tree, none line by line** — `depload.dat` packages (two mods adding
  dependencies to one package both keep them), MOVE states (op by op; two mods both restructuring a
  state that holds internal references keep the higher-priority one whole), world-descriptor missions
  (layers matched by name) and string-table entries.
- `mod build` names where inside a fragment two mods conflicted, and `--json` conflicts add `paths`.

## [1.3.0] - 2026-10-02

### Added
- **AI tab** — tune soldier archetypes and how they fire each weapon, set the odds of optional
  behaviours, and edit brain task parameters in a behaviour tree.
- **`.ai.rml` brain workspaces** — read and recompiled on save; `ai unpack`, `verify` and `lint`.
- **`.spk` sound banks as XML** — `spk decode`, `encode`, `verify` and `new`; every retail bank
  rebuilds byte for byte.
- **Sound bank editor** — the `.spk` panel edits a bank as a tree of what plays what.
- **`mod build` reports every change a layer lost** — two layers shipping different copies of one
  whole file, and a fragment replacing an entry another layer changed in its whole-file copy.
- **The control config files split per section** — one fragment per `<Category>` of
  `defaultusercontrols.xml` and per `<ActionMap>` of an `inputactionmap*.xml`, merged child by child,
  so two mods adding a control or binding to the same section both keep it.

### Changed
- **Breaking:** `mod build --json` conflicts name the losing layers `overruledLayers` (was
  `earlierLayers`) and add a `kind` and a `message`.
- `spk import` and the App's Import… re-derive a sample's loop length and rate, not only its
  length.
- Staging an edit keeps its file selected, so its preview stays open.
- The `.fcb` and Domino sound previews play a random pick from a random container.
- `spk list` names resource kinds.

### Fixed
- An imported looping sample started each loop from silence instead of where its own tail ends.
- Random, switch and multilayer resources read as a link to their child count in the xref index.

## [1.2.0] - 2026-09-26

### Added
- **The Map tab is a scene editor** — hierarchy, inspector and entity library around the viewport;
  select, move, rotate and place entities, edit their fields and components, with undo and redo.
- **Map tab tools** — trigger and light handles, event links, prefabs, a navmesh layer, and a
  **Check** that lists what would fail silently in game.
- **Animations tab** — edit the MOVE graph as rules, replacing the Move graphs tab.
- **`.mab` player** — plays an animation bank's rigs as stick figures.
- **File picker** over the merged archives for any path, hash or sound field.
- **Component properties from the engine** — names, types and enum dropdowns for every class.
- **Domino checks** — entry pins, dead ends and lint problems in the viewer, and `domino check`.
- **Shader CLI** — `shader extract`, `index` and `build`.
- **Dark theme.**
- `.spk` and `.xbt` previews on `.fcb` fields; editor `text_` fields named and greyed out.

### Changed
- The Map tab and `mod lint` read `entitylibrary.fcb`, the library single-player uses.
- The Library tab shows which mods edit an archetype.
- Map tab characters wear their own outfit, skin and colours.

### Fixed
- Female civilians drew only a head and shoulders in the Map tab.
- Map edits with only moves or deletes stayed pending after a save.
- A moved entity without its own `hidAngles` lost its rotation.
- Some MOVE fragments that reshaped a nested state failed to build.
- The released app showed MOVE states as bare hashes.
- `spk import` now writes the imported sound's length.

## [1.1.0-beta] - 2026-09-04

### Added
- **`oasisstrings.rml` is overridden one string at a time** — a localization mod ships one
  `oasisstrings.fragment.xml` per language holding only the strings it changes, instead of all
  11,394. Two mods renaming different weapons never meet. **Breaking:** a whole-file
  `oasisstrings.rml` override is now refused. `jackall-cli rml fragments` writes the patch document,
  and `mod import-legacy` converts one.
- **`<world>.game.xml` splits per mission and per section** — a mod adding an outpost overrides one
  `<Mission>`, and one that raises the shadow radius overrides `_environment.xml`, instead of the
  whole descriptor. Two mods adding different missions finally merge.
- **`_layout.xml`, a container's override unit for what its fragments don't carry** — a sector's
  entities can be re-filed into another mission layer, or deleted one at a time
  (`<delete id="…"/>`), without claiming the whole file. Entity libraries gained group creation and
  `<delete path="…"/>` for archetypes.
- **A world's `omnis`, `managers` and `mapsdata` split per entity** — the last whole-file fallbacks
  the large community mods had. `mapsdata` nests its layers under a per-cell node, so `_layout.xml`
  gained an `under` key. **The fragment cache is invalidated (v5).**
- **Legacy import splits every container, not just `.fcb`** — the fine-grained path runs through
  `IContainerSplitter`, so MOVE graphs, `depload.dat`, the string table and world descriptors import
  as the fragments that actually differ rather than as silent whole-file overrides.
- **Legacy import ignores an editor's float rounding** — floats compare with an 8 ULP interval, and a
  fragment staged for a real edit gets vanilla's own values back everywhere else, so a mod no longer
  arrives claiming edits it never made. Scubrah's Patch drops from 24,924 staged fragments to 7,434.
- **Whole-file fallbacks are reported** — in the app, the CLI and `--json`, with the reason, as are
  the declarations an import leaves behind. All three large community mods now import with none.
- **MOVE animation graphs** — a read-only Move tab, a `move` CLI (`decode`, `encode`, `verify`,
  `clips`, `repoint`, `validate`, `fragments`, `hash`, `names`), and one fragment per state, so
  retargeting one clip no longer ships the whole 1.8 MB graph. `move clips --weapon N` flags clips
  another weapon plays too; `repoint` rewrites only the sites that weapon governs and fails loudly
  when it cannot finish the job.
- **`depload` CLI and fragments** — `decode`, `encode`, `add` and `validate` for the per-world
  dependency index, stageable one resource at a time (~2 KB in place of a 220 KB binary). Writing it
  is what lets a mod ship an animation clip at a path the game never had.
- **`xref reach`, and unused files hidden in the Files tab** — every file classified `used`,
  `used-sp-only`, `used-mp-only`, `unused` or `unknown` by walking the reference graph out from the
  roots `Dunia.dll` itself names. Dead files can be hidden, and editing one asks first. Verdicts for
  the retail corpus ship as `assets/fc2.unused.tsv`.
- **`.xbt`, MOVE and `.rtx` reference extractors** — each closed a source of files that looked
  unreferenced only because nothing parsed the format naming them.
- **`sav clean`, and a Saves tab button for it** — writes a copy of a save with its persisted
  entities dropped, so a modded entity respawns from the current `entitylibrary.fcb`. Mission
  progress, buddies, tapes and diamonds survive. `sav list` lists the saves it finds.
- **`.fc2model` carries the actor** — a pack holding clips also carries the body those clips pose
  (mesh and rig only, ~740 KB), so a weapon's animation is fully visible and a modeller fits the gun
  to hands that are actually in the scene.

### Changed
- **Container splitting is no longer `.fcb`-only** — a format supplies *recognise / open / extract /
  apply* and inherits the three-way merge, load-order folding and `_hash\` addressing unchanged.
- **`depload.dat` browses like a splitting `.fcb`** — one row per resource, under the id a mod stages
  it at, replacing over half a million synthetic rows and the fake folders they grew. Xrefs answer
  for the resource itself, not just the file it sits in.
- **`.spk` reads as events rather than hex rows** — records are grouped under the audio they chain
  to, and the two composite event kinds list what they dispatch to in other banks.
- **A fragment shows what it lives in** — the mission layer or library group it sits in, which its id
  deliberately does not record. An entity whose mission component disagrees with the layer nesting it
  is flagged: the game spawns it from where it sits, so that edit changes nothing on its own.

### Fixed
- A layer holding an `oasisstrings.fragment.xml` can be built again. An inline fragment had no path,
  so its container fell back to the `_hash\<hash>.fcb` name and the build died on "Not an .fcb file".
- An override identical to the base game says so, instead of showing an empty diff.

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
