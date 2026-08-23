# Changelog

Notable changes to FCSE, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
