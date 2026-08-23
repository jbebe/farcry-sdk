# Changelog

Notable changes to the Vortex Far Cry 2 extension, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
