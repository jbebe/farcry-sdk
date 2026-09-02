# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
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
