# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Map viewport "Demo mode" is now the switch for everything that costs.** It used to gate only the
  shadow cascades, the occlusion pass and the sky; every other presentation feature ran regardless
  and was discarded by a trailing `mix()` — the water shader computed its waves, refraction, glint
  and foam, and the frame paid a full-screen colour+depth blit to feed a refraction nothing showed.
  Off, the viewport now draws one geometry pass of flatly lit textures, releases the presentation's
  ~115 MB of render targets, and redraws only when something changes: an idle editor sitting over a
  loaded world1 went from 1.30 s of CPU per 5 s to 0.02 s. Model and scatter geometry draws to the
  near sector ring rather than the far one. Demo mode itself renders identically to before.
- The terrain shader no longer samples the baked lightmap or the surface-type palette when their
  layer toggles are off, nor the twelve-fetch detail blend when Textures is - all three were sampled
  and then scaled away by zero, in both modes.

### Added
- **Map tab** — the start of the map editor: a layer list of everything an FC2 world is built from
  (terrain, entities, water, navmesh, ...), a per-layer context panel, and a 3D viewport that
  renders a loaded world's full terrain heightfield with a fly camera. Read-only so far; layers
  gain editing panels one at a time.
- **`mgb verify`** — checks that a `.mgb`, or the XML it is built from, references only names it
  declares. `mgb encode` proves a package is loadable; this catches the failure that *is* loadable,
  where `Package::ResolveLinks` silently drops a link to an element that does not exist and the
  screen simply misses a control. `--page <NAME>` also requires a `GenericObjectTable` entry keyed
  with the name native code looks the page up under. References into other packages are left alone —
  they are the engine's to resolve — and the reported count of what *was* resolved is how you tell a
  real pass from a vacuous one. Every one of the 50 shipped menu packages verifies clean.
  `MgbVerify` is the reusable core; FCSE's build runs the command over its own layouts before
  encoding them.
- **`mod` CLI branch** — the mod pipeline, previously GUI-only, is now drivable headlessly:
  `mod status`, `mod inspect`, `mod import-legacy`, `mod build` and `mod restore`. Every one takes
  `--json` (one object on stdout, progress on stderr, `{"ok":false,"error":"…"}` and a non-zero exit
  on failure), which is what the [Vortex extension](../vortex-farcry2) is written against.
- **`ModLayerInspector`** — works out whether a folder/zip is a mod layer and, crucially, where its
  tree really starts, by scoring every candidate root against the entries the game actually has.
  Community zips almost always wrap their tree in a folder named after the mod, and stripping the
  wrong number of levels produces a mod that installs cleanly and applies nothing.
- **`LegacyPatchImporter.Import(fatPath, datPath, …)`** — the same import against an
  already-extracted patch pair rather than a zip, for callers handed a folder. The zip entry point
  is now a wrapper over it. `FindPatchPair`/`FindPatchPairInZip` answer "is this a legacy mod?"
  without committing to an import.

### Fixed
- **Loading a second map into the viewport no longer wipes the scene and crashes.** The world swap
  disposed the entity marker layer twice without clearing the field in between, and the ~15 layers
  built between the two calls were handed the GL names the first one had just freed — so the second
  deleted a live layer's program, VAO and index buffer out from under it. What followed was
  `GL_INVALID_VALUE ... Handle does not refer to a shader or program object` and an access violation
  inside `glDrawElementsInstanced`.
- **The shadow and occlusion samplers no longer dangle with Demo mode off.** Their texture unit was
  assigned only on the branch that had a real texture to bind, so with the presentation off
  `shadowMap` and `occlusionMap` pointed at units whose textures had just been freed — or at unit 0,
  where an array sampler finds whatever plain 2D texture a layer left there. Drivers answer an
  incomplete sampler with a message per draw call, which is thousands a frame with debug output
  synchronous, and is why the raw mode ran slower than the Demo mode that draws four times as far.
  Both units now carry a 1x1 stand-in whenever the real one is absent.
- The viewport's fps readout counts only frames the previous one had already asked for. Redrawing on
  demand, the gap after an idle viewport arrived as a frame delta seconds long and was averaged in,
  reporting single-digit rates nobody had waited for. The same delta is now clamped before it steps
  the camera and the water clock, so the first frame after an idle no longer flings both.
- `mod build` refuses to run when `patch.dat` already looks modded and no `patch.dat.vanilla` exists,
  unless `--force` is passed. `PatchBuilder.Build` doesn't guard this itself — `EnsureVanillaBackup()`
  only refuses when given a confirmation callback that returns false, so every headless caller would
  otherwise have baked someone else's mod in as the install's permanent baseline.

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
