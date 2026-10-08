# Changelog

Notable changes to the Vortex Far Cry 2 extension, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.4.0] - 2026-10-08

### Added
- **Installing a mod that contains an FCSE plugin now installs FCSE too**, downloaded from Nexus
  Mods and enabled, if it isn't already there. With a free Nexus account, which can't download
  directly, a prompt offers to open FCSE's Nexus page instead. While an enabled mod has a plugin and FCSE is still
  missing, this repeats every time Vortex starts, and the mod shows a warning icon in the mods
  list that gets FCSE when clicked.
- **Starting the game without FCSE while a mod has a plugin asks first**, offering to start it with
  FCSE instead, since the game on its own never loads plugins.
- **Installing FCSE makes it what Vortex's Play button starts**, unless you already picked another
  primary tool.

## [0.3.0] - 2026-10-02

### Added
- **A mod can override the animation graph one state at a time**, the dependency index one
  resource at a time, and the string table one string at a time, through `movemgr.bin`,
  `<world>_depload.dat` and `oasisstrings.fragment.xml` fragments. Two mods touching different parts
  of these files now merge instead of one overwriting the other.
- **A world's descriptor splits per mission and per section**, and its `omnis`, `managers` and
  `mapsdata` per entity. A `_layout.xml` moves entities between mission layers or deletes them one
  at a time.
- **Legacy mods import as the edits they actually made.** Every container is split, not only
  `.fcb`, and an editor's float rounding is ignored, so an old `patch.dat` mod no longer claims
  thousands of edits it never made and merges with the rest of the load order.
- **Mods that add controls combine.** A mod can ship just the controls category and action map it
  adds to, and two mods adding to the same one both keep theirs.
- **The conflicts notification lists every change a mod lost**, not only two mods editing the same
  part of one entry. It now also lists two mods shipping different copies of one whole file, and an
  entry a fragment replaced inside another mod's whole-file copy, which load order cannot fix. Each
  line says which mod won and what to do about it.

### Changed
- **Breaking: a whole-file `oasisstrings.rml` override is refused.** Ship an
  `oasisstrings.fragment.xml` holding only the strings you change. A legacy mod's string table is
  converted on import.

### Fixed
- **Mods built on these fragments installed without them.** 0.2.0 packed the fragment files into
  `patch.dat` as loose files the game never reads, with no warning. The VSS Vintorez left the player
  with empty hands, a frozen animation and no weapon switching, and kept the name "Dart".

## [0.2.0] - 2026-08-18

### Changed
- **A mod archive is now shaped by two reserved folders at its root, `mods\` and `plugins\`**,
  either alone or together, replacing 0.1.0's literal `Data_Win32\` prefix. `mods\` holds game files
  at any depth and is compiled into `patch.dat`; `plugins\` holds an FCSE plugin's `.dll`/`.lua`
  files and is mirrored into `bin\plugins\`. Archives built for 0.1.0 need their tree moved under
  `mods\`.
- **A `.fcb` container's entities are now standalone files**, one per entity, at a path mirroring
  its place in the library — `generated\entitylibrary.fcb\vehicle\Land\Jeep.xml` — rather than the
  one numbered XML per category that 0.1.0 used, which was the layout Gibbed's extractor produces.
  A mod's own files change shape with it.
- **An FCSE plugin is no longer a mod type of its own.** It rides in a layer's `plugins\` folder, so
  one archive can ship an asset mod together with the plugin that drives it, and disabling that mod
  removes both halves. The FCSE loader itself is still recognized separately and deployed to `bin\`.
- **Load order only decides genuine conflicts now.** Two mods overriding different entities of the
  same `.fcb` container no longer meet at all, and the conflict report names the archetype or placed
  entity that was actually contested rather than the file holding it.
- Installing and deploying were rebuilt against JackAll's reworked `mod` CLI, which is what the
  extension shells out to for everything that touches the game's archives.

## [0.1.0] - 2026-07-30

### Added
- **Installing mods from Nexus**, including the legacy `patch.dat`/`patch.fat` mods most of the
  existing Far Cry 2 catalogue is distributed as, which are converted at install time to keep only
  what differs from the base game.
- **Load order**, applied top to bottom with the bottom mod winning, and a purge that restores the
  game's pristine archives rather than unwinding what was applied.
- **FCSE support** — the loader and its plugins install and deploy through Vortex alongside asset
  mods.
