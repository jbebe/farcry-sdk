# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **Play a `.mab`** — selecting an animation bank in the Files tab draws the rigs it drives as
  stick figures: the bank's own rig in blue, each weapon or prop the tag table names in orange, hung
  from the bone it says. Play, scrub with the slider, orbit and zoom. The same player sits under the
  Animations tab's clip path, so a retargeted clip can be seen before it is applied.
- **Animations tab: edit the MOVE graph as rules** — replaces the Move graphs tab. Pick a weapon and
  a state, set what the character is doing, and every rule is marked as playing, possibly playing or
  beaten by an earlier one, the way the engine searches a state. Retarget a rule's clips, change its
  timing and conditions, copy it as a variant, move it earlier or later, delete it, or copy a
  weapon's whole set of animations to a new index. Save stages only the fragments the edits touch,
  after checking they rebuild the edited graph exactly. The raw object tree is still there behind
  **Raw graph**.
- **The Map tab is laid out like a scene editor** — a hierarchy of every placed entity under its
  mission layer on the left, the viewport in the middle, an inspector on the right and the world's
  entity library along the bottom. The layer toggles moved into a Layers menu over the viewport.
- **Select several entities, move and rotate them** — Ctrl and Shift in the hierarchy, Ctrl+click in
  the viewport; T and R switch between the move arrows and the rotate rings, which act on the whole
  selection. A turn saves as `hidAngles`.
- **Edit an entity's fields** — the inspector shows the instance merged over its archetype, the way
  the game reads it: inherited values greyed, the instance's own overrides marked, each one
  revertable. An edit is staged as that entity's fragment on Save.
- **Place new entities from the library** — drag an archetype onto the ground, or onto a mission
  layer in the hierarchy. The new instance carries only what every shipped instance does and
  inherits the rest; its archetype and meshes are staged the way a paste's are.
- **Hide and lock** entities for the session, and **Modified only** lists what the session changed.
- **Component properties from the engine itself** — `.fcb` names, types and enum dropdowns now also
  come from the properties Far Cry 2 registers for every entity and component class
  (`assets/component_schema.json`), filling in what `binary_classes.xml` lacks. The XML still wins
  wherever it already defines a member.
- **Add and remove components** in the Map tab's inspector. A component shows every property it
  registers; unset ones are faint and keep the engine's default until edited. Collapsing a
  component folds away everything inside it, its groups and slots included.

### Changed
- **The Map tab resolves archetypes against `entitylibrary.fcb`**, the library single-player reads
  and the one a save stages into, instead of `entitylibrary_full.fcb`.
- **The Library tab shows which mods edit an archetype** instead of which game library overrides
  which. Each archetype opens on the definition the game reads; the side panel lists the enabled
  mods editing it in load order, marking an edit dead when it lands on a copy the game doesn't
  read. "Overridden only" became "Modded only", and the "Reads as" picker is gone.
- **`mod lint` checks the library single-player reads** — it used to default to
  `entitylibrary_full.fcb`; the `--profile` option is removed.

### Fixed
- **Saving map edits with nothing added left them pending** — a save of only moves or deletes now
  clears them like any other.
- **A moved entity that had no `hidAngles` of its own lost a rotation** given to it.
- **A MOVE fragment that reshaped a state holding a nested state failed to build**, or could point
  the state machine's slot at the wrong object — the slot is now rebuilt by name only.
- **The released app named no MOVE states** — it did not ship the MOVE names table, so states and
  their fragments showed as bare hashes outside a source checkout.

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
