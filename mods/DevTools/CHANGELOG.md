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
- **Command API**, for the on-screen overlay it exists to be written against. One call runs a
  console line or a Lua chunk on the game thread, from anywhere, with developer-only commands
  reachable regardless of the option above; a catalog of 277 commands describes what to offer and
  what each takes. Only commands that still reach real code are listed - the survey that established
  which of the engine's 416 do is in the developer console page. Nothing here is a setting, and
  nothing appears in the Mod Configuration Menu.
