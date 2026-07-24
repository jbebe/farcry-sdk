# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-07-24

First tagged version. Built up over ~20 commits before this tag existed, so this entry summarizes
what shipped rather than listing each commit individually.

### Added
- **Mods tab** — an ordered, reorderable, enable/disable-able stack of mod zips, plus an
  always-last `workspace\` staging layer for your own edits.
- **Files tab** — every game archive merged into one searchable, browsable filesystem, with
  mod-supplied files highlighted up the whole folder path and a trimmed diff-against-vanilla view
  for modded text/XML/`.fcb` content.
- **`.fcb` editor** — entity libraries split into per-entity fragments, decoded to a structured
  component/field tree (not raw hex), editable and re-importable.
- **Legacy mod importer** — converts an old-style full-replacement `patch.dat`/`patch.fat` mod into
  the workspace's own format by diffing every entry against true vanilla, so only what the mod
  actually changed gets staged.
- **Cross-mod fragment merging** — a three-way merge (vanilla ancestor + each contributing mod)
  for any `.fcb` entity two or more enabled mods both touch, with an explicit conflict error
  instead of one mod silently overwriting another.
- **Format viewers/editors** — `.rml`, `.sbao` (ffmpeg-backed audio import/export/preview), `.xbt`,
  `.xbm`, `.xbg` (orbitable 3D preview), `.sdat`, `.spk`, `.mgb`, plus a syntax-highlighted
  text/XML/Lua editor.
- **Saves tab** — browse `.sav` metadata (world/player, thumbnail, persisted-entity count, DLC),
  delete saves, and hand-edit a save's `PersistenceDB` tree directly.
- **Reproducible, safe builds** — `patch.dat` backed up once; every build regenerates from that
  backup rather than whatever's currently on disk, and the base archives are never written to.
- **Startup caching** (`.appcache`, pre-hashed archive-item/name lookups) so a warm launch is
  ~400ms instead of ~1.2s.
- **`jackall` CLI** — hash-list maintenance (`system hash archiveitems`); everything else is
  still GUI-only.

### Known limitations
- This build was assembled by adding and cutting functionality until it worked decently — it has
  a real automated test suite behind it (`JackAll.Core.Tests`, run against the actual shipped
  archives, not fixtures), but hasn't had a dedicated manual QA pass across the UI itself yet.
  Expect rough edges.
- The CLI only covers hash-list maintenance; every converter and the build step itself are
  GUI-only for now.
- A handful of formats have hard coverage ceilings, not just gaps — see the [JackAll docs
  page](https://jbebe.github.io/farcry-sdk/jackall) ("Where it's going") for specifics.
