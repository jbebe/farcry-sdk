# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
