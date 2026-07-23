---
sidebar_position: 1
---

# Getting Started

:::note[Community-reported]
This page is distilled from community sources (forums, Discord), not independently verified by
reverse engineering. Treated as the most probable explanation where it hasn't been RE-confirmed —
see the [file-formats](/docs/category/file-formats) pages for where RE has directly confirmed or corrected
specific claims.
:::

Distilled primarily from the **OpenWorldGames (OWG) forum**, board "[Single player
modding](https://www.openworldgames.org/owg/forums/index.php?board=169.0)" (~81 threads, active
2011–2017, some threads still get replies today). This is the single richest technical source
found so far for FC2 modding — Gibbed (the tool author) and wobatt (author of an improved decoder)
both post directly in it, and it is the direct successor to the original Something Awful thread
where Gibbed first published his tools (SA put itself behind a $10 paywall shortly after, which is
why OWG became the enduring hub).

This file is also extended with findings from Discord exports covering two active servers — "Far
Cry 2 Multiplayer" and "Far Cry Modding Community" — compacted per-channel via
`tools/DiscordChatExporter/compact_export.py` before being read. Discord-sourced facts are tagged
inline as `(Discord, <server>, "<channel/thread>")` so provenance stays traceable alongside the
OWG-sourced material.

Mod-specific downloads are tracked in [the mods survey](./mods-survey.md); tools/communities are
tracked in [sources](./sources.md). Concrete gameplay-tuning recipes (weapons, vehicles, AI, etc.)
have their own page: [Data Recipes](./data-recipes.md). Known unresolved problems are on the
[Gotchas](./gotchas.md) page, and community theories about the engine's architecture are on
[Engine Theory](./engine-theory.md).

## Toolchain & workflow

- **Gibbed's tools** (by "Rick 'Gibbed'", who posted directly on this forum in 2011):
  `Gibbed.Dunia.ArchiveViewer.exe` (view/extract fat/dat), `Gibbed.Dunia.ConvertBinary.exe` (`.fcb`
  ⇄ `.xml`, object definitions), `Gibbed.Dunia.ConvertXml.exe` (`.rml`/some `.xml` ⇄ `.xml`),
  `Gibbed.Dunia.Pack.exe`, `Gibbed.Dunia.Unpack.exe`. Source was published on Gibbed's "Dunia" SVN
  repository (predecessor of today's `gibbed/Gibbed.Dunia` GitHub repo). License: free to
  redistribute/modify without claiming it as your own; credit appreciated (confirmed by Gibbed
  directly).
- **Bootstrap package**: a helper Gibbed built so modders don't have to manually invoke each tool or
  hand-edit `build_patch.bat`. Extract into `Far Cry 2\modding`, run `bootstrap.bat`, edit XML under
  `modding\mymod\...`, run `build_patch.bat` to produce `patch.fat`/`patch.dat`, copy into
  `Data_Win32` (back up the originals first). Superseded an older, more manual process (see below)
  where modders had to hand-edit `build_patch.bat` to point at a `mypatch` folder instead of
  `mymod`.
- **The very first (pre-bootstrap) workflow**, for reference since some old guides still describe
  it: mirror the original folder path for any file you want to override under `mypatch` (not
  `mymod`), e.g. `modding\mypatch\engine\gamemodes\gamemodesconfig.xml`. Then run
  `build_patch.bat`, copy the resulting `patch.dat`/`patch.fat` to `Data_Win32`, having first
  renamed your existing originals' extensions (e.g. to `.steamdat`/`.steamfat`) so the game won't
  load them. This is effectively what the modern bootstrap package now automates.
- **wobatt's "Far Cry 2 XML File Decoder"** (Delphi, depends on `MIDAS.DLL`) sits on top of Gibbed's
  tools and resolves far more of the hash-only names Gibbed's raw output leaves undecoded, plus
  bundles a modified/improved copy of Gibbed's own tools (fixes a bug where XML→FCB→XML round-trips
  injected spurious nested `<rml>` elements, and a bug where the "no art" extraction option
  defaulted to `true`, blocking art/texture extraction). Concrete before/after from wobatt's own
  comparison (v0.4 vs original Gibbed):
  | Metric | Original Gibbed | wobatt modified |
  |---|---|---|
  | Files identified | 88,284 (55%) | 155,514 (97%) |
  | Object names | 1,562 | 2,120 |
  | Value names | 499 | 2,493 |
  This is a large enough gap that **wobatt's version should be the default starting point**, not
  raw Gibbed tools. wobatt's tool is written in Delphi (distinct from Gibbed's C#).
- **Retail (non-Steam) vs Steam builds**: patch 1.03 differs slightly between them. Concretely
  confirmed failure: even installing the official Ubisoft-hosted v1.03 patch on a retail/GOG/Fortune's
  Edition copy is **not sufficient** — bootstrap can still fail with `"Your patch.fat file doesn't
  seem to have generated\entitylibrarypatchoverride.fcb in it."` The confirmed working fix at the
  time: substitute a community member's already-modded `patch.fat` (which itself derives from the
  Steam-built patch) in place of the "official" retail one, then bootstrap proceeds normally. A
  Steam-derived 1.03 patch data package was also hosted directly for this purpose
  (`mod.gib.me/farcry2/steam_patch_data_1.03.zip` — likely dead now). Forcing the Steam patch onto a
  retail install was also reported to introduce minor UI glitches (garbled "W" characters in the
  main menu, a crash on quit) as a side effect. **Still an active issue as of 2023** (Discord,
  `map-editor`): a user running a cracked copy hit crashes traced to a mismatched `Dunia.dll` +
  `patch.dat`/`patch.fat` set; the fix Gabor gave directly was the same pattern — share a known-good
  Steam-version `Dunia.dll` + `patch.dat` + `patch.fat` triplet to replace the mismatched ones (back
  up originals first). **Root cause of the retail/GOG gap fully resolved** (Discord,
  `🔨-fc2-modding`, Jul 2022, Steve64b + scubrah): it's not just a tooling quirk — **DVD and GOG
  copies are genuinely missing the patched `entitylibrary` data from their patchfile**, even though
  both nominally report version 1.03 like Steam/UPlay (the DLL's DRM-check version string was
  patched to 1.03, but some of the actual 1.03 content files were never included). **Steam and
  UPlay share the same, fully-patched patchfile; DVD/GOG do not.** The garbled "wwwwwwwwwww"
  main-menu bug is specifically a Ubisoft-news-feed UI widget at the bottom of the main menu
  breaking when modding the GOG version, and — tediously — **must be fixed manually per display
  language**, not just once. Also confirmed harmless to delete: the `sanddock`/`sandbar` DLLs — "the
  game runs fine if you delete them." Scoping note from RaZoR-FIN: the actual content difference in
  the missing `.Multi`-variant overrides is "primarily minor edits/fixups and multiplayer balance
  changes" — the only real gameplay difference across versions is **weapon spread in MP**, so this
  gap mostly doesn't matter for singleplayer-focused modding.
- **Common workflow failure**: bootstrap/build_patch failing silently or with permission errors
  almost always traces to the game being installed under `Program Files (x86)` without write access
  for a non-admin user — grant the FC2 folder write access rather than just "running as
  administrator" (confirmed root cause by Gibbed directly).
- **FCBConverter's CRC32 hash-collision problem, root-caused live by its own author** (Discord, Far
  Cry Modding Community, `🔨-fc2-modding`, Jun 2021 — a debugging exchange between **ArmanIII**
  (FCBConverter's author) and **Steve64b** (SCHTEVE's author)): FC2 hashes filenames with **CRC32**
  (unlike FC3+'s CRC64), so cross-game master filelists risk real collisions — a confirmed concrete
  example: `4A724578` maps to both `levels\ige_map\generated\sdat\sd10_shadow.xbt` and
  `scripts\game\barkdata\1436645.bank` within FC2's own filelist. FCBConverter's loader silently
  keeps whichever entry appears **first** in the filelist and drops the rest, so
  wrong-file-identification bugs are possible, worse when a shared multi-game filelist is used.
  **Working fix**: maintain **separate, per-game filelists** rather than one shared master list, or
  place a given game's paths at the list's start/end to bias collision resolution. This is the same
  live exchange that produced the community FC2-only `file.list` (incorporating paths from
  Razorfinnish's and Fino's tools) referenced elsewhere in this project. A related bug found and
  fixed in the same conversation: FCBConverter's single-file-extraction mode (`FCBConverter <fat>
  <output dir> <desired file>`) wasn't passing the detected FAT version through internally for FC2
  specifically, causing it to silently unpack the **entire** fat instead of just the requested file
  — traced to a specific line in FCBConverter's source and fixed by ArmanIII in the same session.
  Separate gotcha: the output-directory argument must be an **absolute path** — a relative path
  fails `Directory.Exists()` silently.
- **A worldsector FCB carries its own internal ID + grid coordinates**: confirmed directly from a
  real hash lookup — `worlds.fat\levels\mp_10_l_fishingvillage\generated\worldsectors\worldsector23.data.fcb`
  resolves to `WorldSector: Id=23 X=3 Y=2`. This is a second, independent confirmation (from actual
  FCB metadata, not just folder-naming convention) of the sector-grid addressing scheme described
  below.
- **`Far-Cry-2-Multi-Fixer` (GitHub, FoxAhead)**: "A utility for launching Far Cry 2 with various
  fixes without modifying the game executable files." Confirmed in live use to resolve a "Dll not
  loaded" crash — a separate, GUI-launcher-style tool distinct from FC2MPPatcher (see
  [sources](./sources.md)), worth trying as a first-line fix for launch-time DLL errors before
  diagnosing further.
- **`.xbg` mesh files can embed their own textures/materials, extractable without hex editing**
  (Discord, Far Cry 2 Multiplayer, `tools-and-mods`, Feb 2026, tool by fdx4061): a standalone
  `EXTRACTOR.exe` takes `.xbg` files placed alongside it, generates a `PATH.ini` on first run (edit
  it to point at your own unpacked resource directories — only the first non-commented line is
  used, prefix a line with `;` to disable it), then on a second run extracts every texture/material
  an `.xbg` references into subfolders that mirror the original directory structure. Previously this
  required manually hex-editing the `.xbg` to locate embedded file references — this is the first
  tool found in this research that automates it specifically.
- **There is no modular/plugin mod system.** The entire community operates on a single monolithic
  `patch.dat`/`patch.fat` — there is no load-order or compatibility layer. If you want two different
  mods' changes together, you must manually merge the underlying XML edits into one patch yourself.
  (This is exactly the gap that ModDB's "Far Cry 2 Universal Patcher" — see [the mods
  survey](./mods-survey.md) — was later built to address.) **A concrete manual-merge technique was
  confirmed by wobatt**: extract both mods' XML into separate folders, then use **WinMerge** to
  line-by-line diff the two mod folders against each other and manually reconcile differences — the
  only community-verified way to combine two independently-built mods' changes into one patch before
  the Universal Patcher existed. **A second, coarser-grained merge technique** (Discord,
  `🔨-fc2-modding`, 2026-07-20/22, from "Yorzar", self-described as "amateurish" but reported working
  after iteration) merges at the folder level instead of line-by-line: install one overhaul mod
  normally, unpack both mods' patch `.dat`/`.fat` archives, then copy specific top-level folders from
  the second mod's unpacked tree over the first's — `_UNKNOWN`, `databases`, `domino`,
  `downloadedcontent`, selected `graphics` subfolders (`_textures`, `actors`, `characters`, `postfx`,
  `sky`, `weapons`), `levels`, `Scripts`, `Ui`, `Worlds` — then repack as `patch.dat`/`patch.fat` into
  `Data_Win32`. Confirms the top-level directory layout of an unpacked FC2 patch archive. Coarser
  than a real diff/merge (whole folders win outright rather than reconciling individual field
  changes), but faster when the two mods don't touch the same files. Reported outcome after fixes:
  high-FPS friendliness, watch pull-out, and vehicle speed issues from the first attempt were
  resolved; remaining known-good/known-bad breakdown was mostly mod-specific balance/UI cosmetics,
  not new format info.
- **No official SDK was ever released.** Every tool here is fan-built reverse-engineering, a
  recurring point of frustration in the threads ("I wish they would have just released an SDK" / "It
  would have been nice for Ubisoft to have released what all the parameters did").
- **A decimal locale gotcha can crash `build_patch.bat`**: using a comma instead of a period in a
  float value (e.g. `1,5` instead of `1.5`) is silently misinterpreted or crashes the build — a real
  trap for European-locale users copy-pasting values.
- **The full converted-field type system** (confirmed by fcmodding.com's FCBConverter docs, a
  second/independent tool built on top of Gibbed's original FCB conversion code): every converted
  `<field>` looks like `<field hash="ABDC41FE" name="fMaxHealth" value-Float32="1000" type="BinHex">00007A44</field>`
  — `hash` is the field name's Hash32, `name` is the decoded readable name (either can be omitted
  except one is required to convert back), `value-FLOATTYPE="…"` is a decoded-for-display
  convenience value that is **ignored** on the round-trip back to binary, and `type`/the raw hex
  body is what actually gets written. To hand-edit a value without doing hex math yourself, replace
  the `type` attribute with the real type and just write the human value directly, e.g. `<field
  hash="ABDC41FE" name="fMaxHealth" type="Float32">1000</field>`. Full type list: `Int16/32/64`,
  `UInt16/32/64`, `Float32/64`, `Vector2/3/4` (comma-separated floats), `String`, `Enum`,
  `Hash32/64`, `Id32/64`, `ComputeHash32/64` (auto-hashes the given value), `Boolean`, and `BinHex`
  (the default, raw hex). This generalizes — and gives the actual mechanism behind — the narrower
  "override type to UInt32/Bool" gotcha noted in [Data Recipes](./data-recipes.md).
- **FAT versioning independently corroborated by a second tool author**: fcmodding.com's
  FCBConverter explicitly documents `-v9` (FC4/FC3/FC3BD) vs `-v5` (FC2) vs default `-v10`
  (FC5/New Dawn) when packing — matching ZenHAX's community research. Also confirms FC2's `-v5`
  archives **cannot use the newer custom Ubisoft FAT/FCB compression** that FC5/ND support —
  irrelevant for FC2 modding, but rules out chasing that as an option.
- **DLC weapon data lives in a separate, tool-resistant archive**:
  `Data_Win32\downloadablecontent\dlc_1\entitylibrary.fcb` (a `worlds\mp_dlc09_jungle\generated\`
  reference confirms the internal path) — this specific file **cannot be decompiled by Gibbed's
  `ConvertBinary` at all**, a hard tooling gap, not just an unexplored area. The confirmed
  workaround (from modder **stoatoats**, author of "RealMod" on ModDB): edit the raw
  `entitylibrary.fcb` directly with a **hex editor**, bypassing the FCB↔XML conversion pipeline
  entirely. stoatoats used this to change crossbow bolt gravity/speed and shotgun pellet count, and
  attempted (but never finished, citing "scripting bugs") an entirely new non-explosive crossbow
  variant obtainable from AI.
- There *is* a DLC modding recipe that works for non-weapon DLC entity data (confirmed step-by-step
  by wobatt): extract `Known\downloadcontent\dlc1\generated\entitylibrary.fcb` from
  `Data_Win32\downloadcontent\dlc1\entitylibrary.fat` via ArchiveViewer, rename to `dlc1.fcb`,
  convert via `Gibbed.Dunia.ConvertBinary.exe --xml .\libraries\dlc1.fcb`, copy the resulting
  `dlc1_converted.xml`/folder into `mymod`, create `mypatch\downloadcontent\dlc1\generated\`, and
  add two lines to `build_patch.bat` (a convert step and a copy step) before the "Creating
  patch.fat/dat..." line. This exposes `1_DLC1Weapons.xml`, `2_vehicle.xml`, `3_WeaponProperties.xml`
  for editing. **However**: modifying `2_vehicle.xml` this way causes a **guaranteed crash** whenever
  a DLC vehicle (Unimog, Quad) spawns near your changes (reproducible near Petro Sahel) — the fix is
  to drop the vehicle file from your DLC mod entirely (losing DLC vehicle changes) while keeping
  weapon/weaponproperties edits, which do work via this path.

## Key files & what lives where

| File / path | Contents |
|---|---|
| `patch.fat\generated\entitylibrarypatchoverride.fcb` | Added by an official Ubisoft patch specifically so any entity in any other `.fcb` could be overridden without touching the (huge) originals — this is the actual mechanism every mod's "patch" exploits. |
| `worlds.fat\world1\generated\entitylibrary_full.fcb` (+ `entitylibrary.fcb`) | Map 1 (Leboa) entity definitions. **Load-order gotcha**: in client/singleplayer mode the game loads `entitylibrary_full.fcb` first and `entitylibrary.fcb` second — so despite the "full" name suggesting primacy, `entitylibrary.fcb` loads *after* it and wins/overwrites on any overlapping entity. Mod edits belong in `entitylibrary.fcb` if you want them to actually take effect over what's in `_full`. |
| `worlds.fat\world2\generated\entitylibrary_full.fcb` (+ `entitylibrary.fcb`) | Map 2 (Bowa) entity definitions — **near-fully duplicated** structure from world1, not shared/parameterized. Same `entitylibrary.fcb`-loads-after-and-overrides-`entitylibrary_full.fcb` load order as world1 applies here too. |
| `modding\libraries\world1\30_player.xml` | Player + all buddy-character definitions. ~11MB — large because every buddy (Warren, Frank, etc.) gets a full duplicated copy of the same rule set. Contains `SensorySystem/FOVParameters` (detection FOV per biome), movement speed values, `fJumpHeight`, `fGravity`. **Per-biome detection FOV concretely confirmed** (guru3D, credit "Merc"): three biome blocks — `DesertFOV`, `SavannahFOV`, `JungleFOV` — each with a `FocusFOV` and `PeripheralFOV` sub-object, each holding `fLength`/`fAngle` floats (defaults e.g. Desert Focus 20/30, Desert Peripheral 20/40, Jungle Focus 5/20, Jungle Peripheral 5/40 — jungle's short `fLength` reflects its dense foliage). Also confirmed reachable via `patch\worlds\tmpla\generated\entitylibrary.fcb` (a shared "template" world folder, distinct from `world1`/`world2`) — convert with `Gibbed.Dunia.ConvertBinary.exe`, edit the extracted `30_player.xml`, convert back, repack. |
| `modding\mypatch\engine\gamemodes\gamemodesconfig.xml` | Only exists after the modding tool's first run (or is copied in manually from `original\patch\engine\gamemodes\`). ~500KB, one of the more human-manageable files. Contains: the arms-dealer "Weapon Bazaar" list (search `cost=`), per-weapon `<Summary>` stat blocks (damage/range/accuracy/reliability/firerate, 0–5 scale — **this block turned out to be cosmetic-only, reflected in the pause-menu UI, not the actual combat math** — see [Data Recipes](./data-recipes.md)), enemy weapon-loadout tables (`<PrimaryWeapon difficulty="…" probability="…" archetype="weapons.Primary.X" />`), mission diamond rewards, `<ChanceToDropGrenade Casual="1" Experimented="0.5" Hardcore="0.33" Infamous="0.25"/>`, `<DefaultCountersService>` block (fall-damage thresholds `fMinSpeedFallDamage`/`fMaxSpeedFallDamage`, per-Act health/reliability rating tables `CounterHealthRatingsTable`/`CounterReliabilityRatingsTable`), `<Infamy>` block (Act level ranges + failure-chance/medicine bands per rep-rate tier), `<StaminaDesertDrainFilters>`, the full **bandolier/max-ammo table** (`<!-- BANDOLIER BONUSES -->`, one `<Plan>` per bandolier type, per-weapon per-difficulty `maxammo` — see [Data Recipes](./data-recipes.md) for the complete reference), the three **manual-bonus systems** (`OPERATIONS MANUALS`, `REPAIR&MAINTENANCE MANUALS`, `VEHICLE MANUALS BONUSES`), `<ReinforcementArchetypes>`/`<MapArmy>` (faction territory data, see [Data Recipes](./data-recipes.md)), `10_Ghostpatrols.xml`-referenced patrol faction assignments, and vestigial cut content (a disabled `Gadget archetype="gadgets.Equipped.Watch"` from pre-release builds — re-enabling it does nothing). |
| `libraries/world1/41_WeaponProperties.xml` (and a `42_weapons.xml`) | The real master weapon-properties file (as opposed to the `mymod` copy) — fire mode (`iBurstLength`: 3 = burst, 0 = full auto; also a separate `selFireRateMode`/`enumFireRateMode` enum: 0=SingleShot, 1=FullAuto, 2=PrepareShot), pellet count (`iBulletsShot`, e.g. shotguns), accuracy/spread (`bUseAngleSpread`, `fAngleYawBulletSpread`, `fAnglePitchBulletSpread`), hit-location severity (`selHitLocation_Torso_Severity`, `selHitLocation_Limb_Severity` — present by default only on sniper rifles), damage tier (`Stim_ImpactDamage`'s `nLevel`), jam mechanics (`fUnjamTime`, `selJamType`, `iClipsForSelfDestruct`, `bIsIndestructible`, `bIsBreakable`, `fInitialJamCounter`), scope zoom (named field `fIronSightFOVdegrees`/`fNearIronSightFOVdegrees` if already decoded, or raw hash `FB4ADD00` as a little-endian IEEE-754 float otherwise — smaller = more zoom), and — via unnamed hashes — magazine size and per-difficulty max ammo (see [Data Recipes](./data-recipes.md)). Each weapon typically has **multiple named copies**: a `.Multi` (multiplayer) variant that ships in the default patch folder, a singleplayer variant with no suffix, and sometimes special-purpose variants like `.AI`, `.Persistent`, or story-specific ones (e.g. Dragunov has a `.Mikes_Rusty` variant) — editing the wrong copy is a very common silent-failure cause. |
| `libraries/world1/weaponpreferences.xml` | Where DLC weapons' non-FCB preferences are set — DLC weapon core stats are in the separately-locked `entitylibrary.fcb`, not this file. |
| `09_gadgets.xml` (under `world1`/`world2`) | Per-gadget max-ammo settings (grenades, molotovs) — editable directly instead of going through the bandolier system. |
| `28_pickups.xml` (under `world1`) | World pickup definitions — small ammo/explosive/fuel pickups (hash-mapped per weapon category, see [Data Recipes](./data-recipes.md)) and named unique pickups like the "Golden AK47" (`Weapons.AK47_new.AK47_Gold`, controlled via `fRespawnTime`). |
| `curves.xml` | Named curves referenced by value (e.g. `Curves.PlayerSicknessCurves.MalariaTimeBeforeFirstAttack`, `Curves.Locomotion.Sprint`) — malaria timers, stamina/sprint curves, and **max health** (`max.health.easy` / `easy-health.bar`-style entries, one per health bar segment) all live here. |
| `world.fat\domino\system\reinforcementregion.lua`, `spawnreinforcement.lua` | **Actual Lua scripts** controlling checkpoint/guard-post reinforcement spawning — confirms Lua exists in FC2 for at least this subsystem (see [Gotchas](./gotchas.md) for the unresolved question of whether patched Lua is even honored). |
| `worlds.fat\worlds\world1\generated\world1.mapdata.fcb` | Patrol vehicle routes, stored as raw XYZ coordinates with no in-game reference frame — editing routes was described as "hours of trial and error" without dedicated tooling. |
| `10_Ghostpatrols.xml` | Lists every patrol type and its faction-color assignment, plus optional vehicle passenger slots (see [Data Recipes](./data-recipes.md), faction infighting recipe). |
| `*.sdat` (per-world-sector terrain data) | See the [`.sdat` format page](../file-formats/sdat.md) — reverse engineering has since confirmed the actual byte layout, correcting an earlier community guess. |
| Vehicle files (`Vehicles_world1.xml`, ~3898 pages/printed-length in one report) | Contains vehicle model/mounting data; the hang glider's flight parameters were **never found** despite extensive searching. A `Chassis`-section `fHealth` value was identified for ground vehicles, but every attempt to recompile a patch changing it **crashed the game** (on load or on vehicle spawn) — unresolved, suspected DLC-folder conflict. |

## Locally preserved reference files (`research/reference-files/`)

A minority of the material mined from Discord exports while researching this project turned out to
be primary-source files worth keeping independently of the (disposable, since-deleted) raw Discord
export: tool archives not hosted on GitHub, concrete format samples, community-maintained hash
lists, and leaked internal build-tool output. These live in `research/reference-files/`, organized
as:

- **`fc2editor-source/FC2Editor_Source.zip`** — genuine, substantial, working-looking C# source (422
  entries) for a WinForms application named `FC2Editor`, built on a custom **"Nomad" engine layer**
  (`FC2Editor.Nomad` namespace: `Camera`, `Engine`, `Render`, `EditorObject`/`EditorObjectPivot`/
  `EditorObjectSelection`, `Gizmo`/`GizmoHelper`, `TerrainManager`/`TerrainManipulator`,
  `TextureManipulator`, `SplineManager`/`SplineRoad`/`SplineZone`, `UndoManager`/
  `EditorEventUndo`, `Validation`/`ValidationReport`, `CollectionManager`/`CollectionManipulator`),
  plus a separate typed **`FC2Editor.Parameters`** framework (`ParamBool`, `ParamFloat`,
  `ParamEnum<T>`, `ParamButton`, `ParamPickButton`). Either the actual stock map editor's source or
  a very close community-built equivalent — the first real (non-reverse-engineered) source-level
  view into how the editor's object/gizmo/undo/validation systems are structured.
- **`hash-lists/`** — community-maintained CRC32→filename lookup lists: `worlds.filelist` (+ two
  earlier revisions `worlds_v2`/`worlds_v3`), `worlds_english.filelist`, `entitylibrary.filelist`,
  `dlc1.filelist`, `dlc_jungle.filelist`, `patch.filelist`, `map_files.filelist`, and two early
  general-purpose `master_file_v1/v2.list` files.
- **`format-samples/`** — real, working instances of formats documented in [File
  Formats](/docs/category/file-formats): `ak47.xbg`/`computer.xbg` (mesh), 4× `.xbt` texture samples, decoded
  FCB→XML data (`25_buddies.xml`/`world1.xml`/2× `soundpoint.xml`), real engine Lua scripts
  (`master_world1.world1.lua`/`master_world2.world2.lua`/`common_hq_doorman...lua`), a real archive
  pair (`patch.dat`+`patch.fat`), `movemgrnamed.bin`, a navmesh file (`nv_4979.nvm`), a `.sbao`
  sample (`004ae237.sbao`), a worked prefab-manager editing example (`tmpla.managers.zip`), and a
  worked texture-swap example (`graphics_xbm_swap_example.zip`).
- **`tool-archives/`** — small tools shared as direct Discord attachments rather than on GitHub:
  `EXTRACTOR.7z` (fdx4061's `.xbg` texture/material extractor), `XBT-Thumbnail-Provider.zip`,
  `xbmEditor1.7z` (fdx4061's XBM editor), and `SkeleTree.zip` (fdx4061's cross-game skeleton reading
  tool, confirmed working on Avatar/FC2/FC3).
- **`text-snippets/`** — `CRC32_collisions.txt`, `Far_Cry_2_1.03_filediffs.txt` (the file-level diff
  behind the retail/GOG patch-gap root-cause above), plus overflow `message.txt` attachments:
  `crc32_collision_examples.txt` (~20 real hash→dual-path collision pairs), `fcbconverter_collision_repro.txt`
  (a reproduced live collision failure log), `redux_readme.txt` (Redux's full changelog, confirming
  its author as "Hunter (BigTinz)"), `dunia_internal_tool_plugins_list.txt` (a leaked internal Dunia
  build-tool plugin list — ~50 build-pipeline plugins, including confirmation that FC2's `.nvm`
  navmesh files are built on the open-source **Recast** navmesh library), `cloth_shader_disasm_example.txt`
  (a readable disassembled `ps_4_0` HLSL pixel shader for FC2's `Cloth` material — shader bytecode
  is not opaque-packed binary).

A leaked internal build-tool plugin list also confirms several naming conventions inferred
elsewhere in this research (e.g. `NomadDB` ties directly to the `FC2Editor.Nomad` namespace above)
and is strong primary-source evidence for the internal compiler-plugin architecture behind every
`.fcb`/`.xbg`/`.xbt`/`.nvm` file the community has been reverse-engineering from the outside.
