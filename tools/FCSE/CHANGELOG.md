# Changelog

Notable changes to FCSE, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **The Mod Configuration Menu scrolls.** Past the page's seventeen lines it now moves a window over
  its own list of rows, so a mod's settings are no longer cut off. Ported from FC2JackalFix, which
  solved the same limit first.
- **A plugin can grey out a settings row, or keep it off the page** — `FCSE_SettingFlag_Disabled`
  draws it locked and unselectable, value and all; `FCSE_SettingFlag_Hidden` omits it, leaving
  `fcse.ini` as its only interface. Either way the setting keeps working everywhere else. Lua mods
  get both as `fcse.setting{ … disabled = true, hidden = true }`.

### Changed
- Plugin ABI is now `FCSE_API_VERSION` 7: `FCSE_Setting` gained a `flags` field. Existing plugins
  need no source change but must be rebuilt.

## [1.1.0] - 2026-08-23

### Changed
- Internals only — sources regrouped, the settings page split into four, more of FCSE brought under
  test. The plugin ABI, the Lua API and both examples are unchanged, so existing mods keep working.

## [1.0.0] - 2026-08-11

### Added
- **Two ways to write a mod**, loaded from `bin\plugins\` at any depth: a Lua script, or a DLL
  exporting `FCSE_Load`. The same API either way.
- **Hooks, memory patches, byte-signature scanning and engine function calls** — how a mod changes
  engine behaviour without anyone shipping a patched `Dunia.dll`.
- **Settings rows a mod registers into the game's own Options screens**, persisted to `fcse.ini`.
- **An address library keyed by symbol rather than address**, so one build runs on both PC builds of
  v1.03.
- **`bin\fcse.log`** — what each script and plugin registered, hooked or patched, and where two of
  them collide.
