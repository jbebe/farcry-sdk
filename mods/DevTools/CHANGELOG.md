# Changelog

Notable changes to DevTools, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- First release. DevTools is the developer half of what UFCP was carrying, split into a plugin of
  its own: UFCP is what a player installs, this is what a modder or a reverse engineer installs.
- **Savegame launch** fix. `-load <name>.sav` quit to desktop instead of booting straight into the
  save, which is the fastest way to iterate on one.
- **Developer console** option, off by default. Far Cry 2's own console opens on `~`, but roughly 57
  of its commands are marked developer-only and answer "Unknown command" — `load_level`,
  `set_health`, `teleport_to_current_objective`, `aidebugtool`, `console_dump_elements` among them.
  Turning this on lists and runs them. The separate filter that keeps multiplayer-only and
  editor-only commands out of a single-player console is left alone.
