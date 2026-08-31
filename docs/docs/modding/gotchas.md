---
sidebar_position: 7
---

# Known Gotchas & Unresolved Problems

:::note[Community-reported]
Sourced from the OWG forum and Discord communities — see [Getting Started](./getting-started.md) for
the full provenance note.
:::

## Savegame behavior

- **Some values are cached in savegames** and only "wear off" after continued play or a new game
  (confirmed by Gibbed himself, not just community guesswork) — e.g. a jump-height change is reliably
  visible only from a new game; reloading an existing save was repeatedly reported as unreliable
  (sometimes needing many jump-to-exhaustion cycles to "reset" the cached number, sometimes not
  working until a fresh career). Autoreload and similar per-instance flags may require picking up a
  *new copy* of the weapon (e.g. buying one) rather than reloading a save. Don't assume a mod "doesn't
  work" from one quick test with an old save. See [savegame](../file-formats/savegame.md) for the
  confirmed mechanism (a per-entity, per-property overlay, not a global freeze).
- **Directly setting a high reputation via save-edit doesn't reproduce organic NPC reactions** — see
  [Data Recipes](./data-recipes.md#economy--progression).

## Modding-tool gotchas

- **DLC weapon content is not supported by the normal Gibbed round-trip** and requires direct
  hex-editing of `entitylibrary.fcb` (see [Getting Started](./getting-started.md)). A theory for why
  patch-level overrides to DLC weapons fail even when the file itself is editable: DLC content loads
  *after* the main patch, so the DLC package's own baked-in data wins at runtime regardless of what
  your patch says.

  :::info[Verified via reverse engineering]
  Confirmed directly: `CXGame::LoadArchetypes` loads the base entity library first, then calls
  `CDlcService::GetEntityLibraries()` and merges each installed DLC's own library on top via a real
  `CEntityLibraryManager::Override` call — see [archives](../file-formats/archives-fat-dat.md).
  :::

- **`gamemodesconfig.xml` exists in more than one archive** (`Common.dat`/`.fat` and
  `World.dat`/`.fat` both contain a copy) — which copy "wins" at runtime caused real confusion; one
  guru3D user reported visible corruption (white/missing textures at the ESRB splash) when repacking
  `Common.dat/fat` with an edited copy, suggesting that archive is more sensitive to repacking than
  `World`. Safer default: edit via the standard bootstrap/`mypatch` override mechanism (which targets
  the override `.fcb`, not a raw archive repack) rather than hand-repacking `Common.dat/fat` directly.

  :::info[Verified via reverse engineering]
  Resolved at the disassembly level — see [archives](../file-formats/archives-fat-dat.md)'s confirmed
  archive search-path order (`patch.dat` > `common.dat` > `sound*.dat`/`soundcache.dat`/
  `shadersobj.dat` > `worlds/*.dat`, first match wins): `common.dat` is checked before any
  `worlds/*.dat`, so its copy of a colliding hash wins over `World.dat`'s.
  :::

- **XBT texture format was historically only solved one-way**: extracting/converting `.xbt` → `.dds`
  was possible (e.g. via a 010 Editor template) well before `.dds` → `.xbt` repacking was documented —
  though the community *did* successfully reskin many weapon textures, implying a repacking method
  existed by ~2011–2012 even without a clear writeup. Check whether `xbt2dds` (used by SCHTEVE, per
  [Sources](./sources.md)) has since closed this gap in both directions.
- **Magazine capacity, weapon fire-mode, and similar "deep" values are all hash-only** in Gibbed's raw
  output — expect to need the `BinHex`→`UInt32` type-override trick ([Getting
  Started](./getting-started.md)) regularly for anything not already named by wobatt's improved tool.
- **`.mgb` and `.mgb.desc` are not both hex-editor-only**, contrary to the [Almost Complete
  Guide](./guide/file-management.md)'s claim. Only `.mgb` (binary, `"MAGMA"`-magic) is.

  :::info[Verified via reverse engineering]
  `.mgb.desc` is plain, well-formed XML — verified by extracting real samples from `patch.fat`
  (`ui\localized\pc\eng\ui\options.mgb.desc` etc.) and reading them directly. `.mgb` itself has since
  had its byte-level layout fully deciphered — see the [`.mgb` format page](../file-formats/mgb.md).
  :::

- **A leaked FC2 press-review (pre-release) build partially breaks FCBConverter**: a leaked 3.4GB
  press-review archive unpacks, but many files come out with "incorrect data," possibly because
  FCBConverter misdetects the archive version (its `version.ini` differs from retail's); some files
  extract fine because their position happens to match the retail layout, others don't. Unresolved
  working theories: a different fat data layout, or a different compression method. Low-priority —
  only matters if this specific leaked build ever needs mining.

## Engine/binary quirks

- **There is no in-game dev console** in Far Cry 2 — rules out console-command-based testing/debugging
  workflows some other Dunia-era titles might support; testing changes always means a full
  repack-and-relaunch cycle, or the map editor's `CTRL+G` live-playtest shortcut (see [Engine
  Theory](./engine-theory.md)).

  :::info[Verified via reverse engineering]
  Confirmed at the binary level — see [command-line args](../engine-internals/command-line-args.md)'s
  `-logFile` investigation: no logging facility or console sink exists anywhere reachable from boot in
  the retail build.
  :::

- **Weapon pickup/UI icons are partly hardcoded in `Dunia.dll` itself** — see [Data
  Recipes](./data-recipes.md#weapon-slot-and-sound).
- **Vehicle max-HP modding is unreliable**: changing a ground vehicle's `fHealth` (Chassis section) and
  recompiling reliably crashes the game (on load or on vehicle-spawn), even though the base game's
  multiplayer vehicle files are modified by the default patch without issue — suspected DLC-folder
  conflict, never resolved.
- **Object draw-distance was reportedly never successfully modded**, as of a 2023 report — distinct
  from the LOD/terrain/tree/cluster distance settings in `defaultrenderconfig.xml` documented in [the
  Almost Complete Guide](./guide/graphics.md) (`LodScale`, `TerrainDetailBlendViewDistance`,
  `RealTreesLodScale`, etc.), which are specifically about terrain, not ordinary placed *objects*. Not
  independently re-verified since 2023.

  :::info[Verified via reverse engineering]
  The stock map editor's decompiled source (`ToolObject.cs`) confirms placed objects have an explicit
  "Occlusion" category, flagged by a specific hash ID (`0xC3C41DC8`) on the object's inventory folder —
  a distinct perf-culling mechanism from LOD-distance settings, and a plausible reason ordinary
  per-object draw-distance tuning has no obvious data-driven knob: culling may be occlusion/
  category-driven rather than pure distance-driven for placed objects.
  :::

- **Stuck-on-splash-screen boot failure, fixed by swapping `systemdetection.dll` from Far Cry 3**: a
  Steam copy that had run fine started hanging on the splash screen after a reinstall — no window, no
  error. Verifying file integrity, reinstalling, trying compatibility modes, and clearing the Documents
  profile all failed. Working fix: install Far Cry 3, copy **its** `systemdetection.dll` over Far Cry
  2's own copy — the game then booted normally. Cause unconfirmed (bad/outdated hardware-detection
  logic in FC2's own DLL is the working theory). Two other unconfirmed boot-failure causes mentioned in
  the same thread: a damaged profile XML in the Documents save folder, and a separate, unrelated
  "infinite fire" bug when hosting a dedicated server, worked around by setting Windows compatibility
  mode to Vista SP1 (SP-hosting-specific, not a general splash-screen fix).

## Lua reliability

**Lua/script overriding is inconsistent across reports.** In 2011, a modder (Rhynder) found that a
modified `spawnreinforcement.lua`/`reinforcementregion.lua` placed via the normal patch mechanism was
silently ignored — the game "bypasses it and reads the original." In 2016, a different modder
(hans_dampf36) reported successfully changing in-game behavior (making an object disappear) via
patched code. This discrepancy is unresolved — could be tooling improvements between 2011–2016, could
be file/subsystem-specific behavior. Worth testing directly rather than assuming either result
generalizes.

**One Lua-driven subsystem is confirmed fully reliable, though**: outpost recapture/respawn timing is
implemented via Lua timers with no hard length limit (chain/loop the same timer indefinitely for
arbitrarily long delays), confirmed working in a real mod demo (Discord, `🔨-fc2-modding`, Jul 2022,
"Far Cry 2 Delayed Outpost Respawning" by scubrah). The timer state is saved in the savegame itself and
survives quicksave/reload correctly — the only way to reset an outpost's cleared-timer is loading a
save from *before* it was cleared. This is a concrete counterexample to treating Lua reliability as
uniformly flaky — it's subsystem-specific.

## Unsolved/inert content

- **Some values appear to do nothing at all** even when changed correctly — the vestigial "watch"
  gadget entries are inert (pre-release cut content). Setting a Guard Post's vehicle-chase value to
  `0` stops vehicle pursuit specifically (patrols still chase on foot) — one tester reported `100` also
  "worked," an inconsistency never fully explained.
- **The "Jackal Tape Glitch" / "boots bug"** (the same collectible-tape audio recording plays
  repeatedly instead of advancing) was investigated multiple times across multiple years (2011 and
  again 2016) and never solved. Known to be tied to the game being patched to v1.3+ (only present on
  1.3+; 1.2 avoids it, but 1.2 is incompatible with the modding tools) — a real modding-vs-correctness
  trade-off with no resolution. (Small consolation for going to 1.3: it also removes the SecuROM DRM.)
  One unconfirmed theory: a broken start/end pointer into a single concatenated audio file.
- **The hang glider was never successfully modded** — see [Data Recipes](./data-recipes.md#player).
- Retail PC XML files were found to contain entire unused sections for other platforms (AGORA, Xbox)
  bundled in alongside the PC data — the shipped data files were not platform-trimmed.

## NPCs and multiplayer

- **NPCs placed via the map editor only function in the map editor's own test/singleplayer-style
  context — they freeze in actual multiplayer.** If a goal is adding functional NPCs to custom MP maps,
  this is a hard current limitation, not a bug to work around.

## Map editor tool quirks

Confirmed directly from the stock map editor's decompiled source
(`tools/third-party/FC2Editor_Source/FC2Editor.Tools/` — see [Getting
Started](./getting-started.md) for provenance):

- **Every paint-brush tool (terrain sculpt, texture, foliage) shares one input scheme**, inherited
  from a common base class (`ToolPaint.cs`): Shift+drag doesn't paint — it resizes the brush live (0.5
  units per pixel of the dominant drag axis). Easy to trigger by accident expecting it to paint at a
  larger size.
- **Ctrl+brush doesn't erase generically on the Texture or Foliage painters** (`ToolTexture.cs`,
  `ToolCollection.cs`) — it hardcodes the painted ID to a reserved "slot 0"/"empty" ID rather than
  inverting brush strength. If texture slot 0 holds a real assigned texture, "erasing" with Ctrl paints
  *that* texture instead of clearing to nothing.
- **The Noise terrain tool's third dropdown mode ("Raise/Lower" combined) looks non-functional in the
  shipped editor code** (`ToolTerrainNoise.cs`) — the underlying enum-value array never assigns it a
  distinct value from plain "Raise," so it's likely aliased rather than genuinely combining both
  directions. Worth confirming visually before relying on it for precision work.
- **Roads and the Playable Zone boundary share a hard 100-point cap per spline** (`ToolSpline.cs`),
  enforced in code, not just a soft UI limit — a long winding road or an intricate zone boundary can
  hit this ceiling.
- **Water Level's valid range is -1 to 255, and -1 specifically means "no water"**
  (`ToolEnvironment.cs`) — distinct from setting an actual height of 0.
- **Arrow keys in Move mode have an easy-to-trigger modifier trap** (`ToolObject.cs`): unmodified
  arrows nudge the object 1 unit/degree per tick (Shift = 1/4 speed, finer control), but Ctrl+Left/Right
  *rotates* around Z instead of moving, and Ctrl+Up/Down moves *vertically* instead of horizontally — an
  asymmetric scheme not obvious from the UI.
- **Object Snap mode's "Preserve Orientation" and angle-snap are mutually exclusive, not combinable**
  (`ToolObject.cs`) — turning on Preserve Orientation silently disables the angle-snap fields rather
  than just ignoring them (Snap mode drags from one object's nearest anchor/pivot point to another's).
- **The Move/Rotate tools' anchor-point snapping is driven by per-object-type "pivot" data baked into
  the global object database, not per-map data** (`ToolObject.cs`) — editable only via a hidden 6th
  Object-tool mode gated behind the `-editobjectdb` command-line flag, which the editor's own source
  explicitly labels "used only for development purposes... will not be included in retail." This
  explains why ordinary map editing can't customize an object's snap-anchor points.
