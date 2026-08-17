---
sidebar_position: 1
---

# Getting Started

:::note[Community-reported]
Distilled from the **OpenWorldGames (OWG) forum**, board "[Single player
modding](https://www.openworldgames.org/owg/forums/index.php?board=169.0)" (~81 threads, active
2011–2017, some still get replies today) — the successor to the original Something Awful thread
where Gibbed first published his tools — plus Discord exports from two active servers, "Far Cry 2
Multiplayer" and "Far Cry Modding Community". Not independently verified by reverse engineering;
treated as the most probable explanation where it hasn't been RE-confirmed. See [the
intro](../intro.md) for how RE-verified and community-reported claims are distinguished across this
site. Discord-sourced facts are tagged inline as `(Discord, <server>, "<channel/thread>")`.
:::

Mod-specific downloads are tracked in [Mods Survey](./mods-survey.md); tools and communities in
[Sources](./sources.md). Concrete gameplay-tuning recipes are on [Data Recipes](./data-recipes.md).
Known unresolved problems are on [Gotchas](./gotchas.md), and engine architecture theory plus the map
editor are on [Engine Theory](./engine-theory.md). If you'd rather use a mod manager than hand-edit
`patch.dat`, see [Vortex](./vortex.md) for the tooling built in this repo.

## The modding model

There is no loose-file override and no plugin system. The engine reads every asset out of packed
`.dat`/`.fat` archives, and the only one a mod may touch is `Data_Win32\patch.dat`/`patch.fat`.
"Installing a mod" means recompiling that one archive pair. This is possible because an official
Ubisoft patch added `patch.fat\generated\entitylibrarypatchoverride.fcb` specifically so any entity in
any other `.fcb` could be overridden without touching the (huge) originals — this override hook is the
mechanism every mod, and every modding tool, ultimately exploits.

Because there's no load order or compatibility layer, combining two independently-built mods means
manually merging their underlying XML edits into one patch (this is exactly the gap [Far Cry 2
Universal Patcher](./mods-survey.md) was later built to address). No official SDK was ever released —
every tool covered here is fan-built reverse engineering.

## Toolchain

- **Gibbed's tools** (Rick "Gibbed"): `Gibbed.Dunia.ArchiveViewer.exe` (view/extract `.fat`/`.dat`),
  `Gibbed.Dunia.ConvertBinary.exe` (`.fcb` ⇄ `.xml`), `Gibbed.Dunia.ConvertXml.exe` (`.rml`/some
  `.xml` ⇄ `.xml`), `Gibbed.Dunia.Pack.exe`, `Gibbed.Dunia.Unpack.exe`. Free to redistribute/modify
  without claiming it as your own; credit appreciated.
- **wobatt's "Far Cry 2 XML File Decoder"** (Delphi, needs `MIDAS.DLL`) sits on top of Gibbed's tools
  and resolves far more of the hash-only names Gibbed's raw output leaves undecoded, plus bundles a
  fixed copy of Gibbed's own tools (corrects a bug where XML→FCB→XML round-trips injected spurious
  nested `<rml>` elements, and one where the "no art" extraction option defaulted to `true`). This
  should be the default starting point over raw Gibbed tools:

  | Metric | Original Gibbed | wobatt modified |
  |---|---|---|
  | Files identified | 88,284 (55%) | 155,514 (97%) |
  | Object names | 1,562 | 2,120 |
  | Value names | 499 | 2,493 |

- **Bootstrap package**: extract into `Far Cry 2\modding`, run `bootstrap.bat`, edit XML under
  `modding\mymod\...`, run `build_patch.bat` to produce `patch.fat`/`patch.dat`, copy into
  `Data_Win32` (back up the originals first). This superseded an older manual process, still described
  in some guides: mirror the original folder path for any file you want to override under
  `modding\mypatch` instead (e.g. `modding\mypatch\engine\gamemodes\gamemodesconfig.xml`), run
  `build_patch.bat`, then rename your existing archive files' extensions (e.g. to
  `.steamdat`/`.steamfat`) before copying the new pair in, so the game won't load the originals.

### Round-tripping `.fcb` values by hand

Every converted `<field>` looks like `<field hash="ABDC41FE" name="fMaxHealth" value-Float32="1000"
type="BinHex">00007A44</field>` (confirmed by fcmodding.com's independent FCBConverter docs) — `hash`
is the field name's CRC32, `name` the decoded readable name (either can be omitted, but one is
required to convert back), `value-FLOATTYPE="…"` is a decoded display convenience **ignored** on the
round-trip to binary, and `type`/the raw hex body is what actually gets written. To hand-edit a value
without doing hex math, replace `type` with the real type and write the human value directly:
`<field hash="ABDC41FE" name="fMaxHealth" type="Float32">1000</field>`. Full type list: `Int16/32/64`,
`UInt16/32/64`, `Float32/64`, `Vector2/3/4` (comma-separated floats), `String`, `Enum`, `Hash32/64`,
`Id32/64`, `ComputeHash32/64` (auto-hashes the given value), `Boolean`, `BinHex` (the default, raw
hex). `ConvertBinary` gives no line numbers on malformed XML, just a raw stack trace — re-run after
every small edit to isolate mistakes. A decimal-locale gotcha: a comma instead of a period in a float
(`1,5` instead of `1.5`) is silently misinterpreted or crashes the build.

## Common failures

- **Bootstrap/build_patch failing silently or with permission errors** almost always traces to the
  game being installed under `Program Files (x86)` without write access for a non-admin user — grant
  the FC2 folder write access rather than "running as administrator" (confirmed root cause by Gibbed).
- **Retail (non-Steam) vs Steam builds differ**, even at the same reported patch version. Installing
  the official v1.03 patch on a retail/GOG/Fortune's Edition copy is not sufficient — bootstrap can
  still fail with `"Your patch.fat file doesn't seem to have generated\entitylibrarypatchoverride.fcb
  in it."` **Root cause**: DVD and GOG copies are genuinely missing the patched `entitylibrary` data
  from their patchfile, even though both nominally report version 1.03 like Steam/UPlay (the DLL's
  DRM-check version string was patched, but not all of the actual 1.03 content files were included).
  Steam and UPlay share the same, fully-patched patchfile; DVD/GOG do not. **Fix**: substitute a
  known-good Steam-derived `patch.fat` (and, if crashes persist, a matching `Dunia.dll` +
  `patch.dat`/`patch.fat` triplet) in place of the retail one. Forcing a Steam patch onto a retail
  install can introduce minor UI glitches (garbled "W" characters in the main menu — a Ubisoft
  news-feed widget breaking, fixable but must be redone per display language — plus a crash on quit)
  as a side effect. The actual content difference in the missing `.Multi`-variant overrides is mostly
  minor fixups and MP balance changes; the only real singleplayer-relevant gap is negligible, so this
  mostly doesn't matter outside multiplayer.
- **DLC weapon data is locked**: `Data_Win32\downloadablecontent\dlc_1\entitylibrary.fcb` cannot be
  decompiled by `ConvertBinary` at all — a hard tooling gap. The confirmed workaround (modder
  stoatoats, author of "RealMod"): edit the raw file directly with a hex editor, bypassing the FCB↔XML
  pipeline entirely. Used to change crossbow bolt gravity/speed and shotgun pellet count.
- **Non-weapon DLC entity data does have a working recipe** (confirmed step-by-step by wobatt):
  extract `Known\downloadcontent\dlc1\generated\entitylibrary.fcb` from
  `Data_Win32\downloadcontent\dlc1\entitylibrary.fat` via ArchiveViewer, rename to `dlc1.fcb`, convert
  via `Gibbed.Dunia.ConvertBinary.exe --xml .\libraries\dlc1.fcb`, copy the resulting
  `dlc1_converted.xml`/folder into `mymod`, create `mypatch\downloadcontent\dlc1\generated\`, and add
  a convert step and a copy step to `build_patch.bat` before the "Creating patch.fat/dat..." line.
  Exposes `1_DLC1Weapons.xml`, `2_vehicle.xml`, `3_WeaponProperties.xml` for editing. Modifying
  `2_vehicle.xml` this way guarantees a crash whenever a DLC vehicle (Unimog, Quad) spawns near your
  changes (reproducible near Petro Sahel) — drop the vehicle file from your DLC mod and keep only the
  weapon edits, which do work via this path.
- **FCBConverter's CRC32 hash-collision problem**, root-caused live by its own author (Discord, Far
  Cry Modding Community, `🔨-fc2-modding`, Jun 2021 — ArmanIII and Steve64b): FC2 hashes filenames
  with CRC32 (unlike FC3+'s CRC64), so cross-game master filelists risk real collisions — e.g.
  `4A724578` maps to both `levels\ige_map\generated\sdat\sd10_shadow.xbt` and
  `scripts\game\barkdata\1436645.bank` within FC2's own filelist. FCBConverter's loader silently keeps
  whichever entry appears first and drops the rest. **Fix**: maintain separate, per-game filelists
  rather than one shared master list. The same session found and fixed a related bug: single-file
  extraction (`FCBConverter <fat> <output dir> <desired file>`) wasn't passing the detected FAT
  version through for FC2 specifically, silently unpacking the entire fat instead of the requested
  file. Separately, the output-directory argument must be an absolute path — a relative path fails
  `Directory.Exists()` silently.
- **A worldsector `.fcb` carries its own internal ID + grid coordinates**, confirmed from a real hash
  lookup: `worlds.fat\levels\mp_10_l_fishingvillage\generated\worldsectors\worldsector23.data.fcb`
  resolves to `WorldSector: Id=23 X=3 Y=2`.
- **`.xbg` mesh files can embed their own texture/material references, extractable without hex
  editing** (Discord, Far Cry 2 Multiplayer, `tools-and-mods`, Feb 2026, tool by fdx4061): a standalone
  `EXTRACTOR.exe`, placed alongside a batch of `.xbg` files, generates a `PATH.ini` on first run (edit
  it to point at your unpacked resource directories — only the first non-commented line is used), then
  on a second run extracts every referenced texture/material into subfolders mirroring the original
  directory structure.
- **`Far-Cry-2-Multi-Fixer`** (GitHub, FoxAhead) launches the game with various fixes without
  modifying the executable files — confirmed to resolve a "Dll not loaded" crash. A separate,
  GUI-launcher-style tool from FC2MPPatcher (see [Sources](./sources.md)).

### Manually merging two mods

Before [Far Cry 2 Universal Patcher](./mods-survey.md) existed, two techniques were used:

- **Fine-grained** (wobatt): extract both mods' XML into separate folders, then use WinMerge to
  line-by-line diff the two folders against each other and manually reconcile differences.
- **Coarse-grained** (Discord, `🔨-fc2-modding`, 2026-07-20/22, "Yorzar"): install one overhaul mod
  normally, unpack both mods' patch archives, then copy whole top-level folders from the second mod's
  unpacked tree over the first's (`_UNKNOWN`, `databases`, `domino`, `downloadedcontent`, selected
  `graphics` subfolders, `levels`, `Scripts`, `Ui`, `Worlds`), then repack. Faster than a real
  line-by-line merge when the two mods don't touch the same files, but whole folders win outright
  rather than reconciling individual field changes.

## Key files & what lives where

For a full categorized survey of the install directory by file type, see [File
Manifest](./file-manifest.md). This table covers the specific gameplay-tuning files worth knowing by
name:

| File / path | Contents |
|---|---|
| `patch.fat\generated\entitylibrarypatchoverride.fcb` | The official override hook every mod exploits (see above). |
| `worlds.fat\world{1,2}\generated\entitylibrary{,_full}.fcb` | Per-map (Leboa/Bowa) entity definitions. **Load-order gotcha**: the two bases do *not* stack — `CXGame::LoadArchetypes` branches on a flag and loads one or the other, never both, and the patch override then loads afterwards and wins over whichever was chosen. See [Archetype resolution](../engine-internals/entity-instancing.md). World2 is a near-fully duplicated structure from world1, not shared/parameterized. |
| `modding\libraries\world1\30_player.xml` | Player + all buddy-character definitions (~11MB — every buddy gets a full duplicated rule set). Contains `SensorySystem/FOVParameters` (per-biome detection FOV), movement speed values, `fJumpHeight`, `fGravity`. Also reachable via `patch\worlds\tmpla\generated\entitylibrary.fcb` (a shared "template" world folder, distinct from `world1`/`world2`). |
| `modding\mypatch\engine\gamemodes\gamemodesconfig.xml` | Only exists after the tool's first run (or copied manually from `original\patch\engine\gamemodes\`). ~500KB. The arms-dealer list, per-weapon `<Summary>` stat blocks (cosmetic-only, pause-menu UI — not the real combat math), enemy weapon-loadout tables, mission diamond rewards, grenade-drop chance, fall-damage/health/reliability rating tables, infamy bands, the bandolier/max-ammo table, the three manual-bonus systems, faction territory data, patrol faction assignments, and vestigial cut content (a disabled pre-release `Watch` gadget that does nothing when re-enabled). See [Data Recipes](./data-recipes.md) for specifics. |
| `libraries/world1/41_WeaponProperties.xml` (+ `42_weapons.xml`) | The real master weapon-properties file. Fire mode, pellet count, spread, hit-location severity, damage tier, jam mechanics, scope zoom, and (via unnamed hashes) magazine size/max ammo — see [Data Recipes](./data-recipes.md). Each weapon typically has multiple named copies (`.Multi` for multiplayer, an unsuffixed singleplayer variant, sometimes `.AI`/`.Persistent`/story-specific variants) — editing the wrong copy is a common silent-failure cause. |
| `libraries/world1/weaponpreferences.xml` | DLC weapons' non-FCB preferences — core DLC weapon stats are in the separately-locked `entitylibrary.fcb`, not here. |
| `09_gadgets.xml` (under `world1`/`world2`) | Per-gadget max-ammo settings (grenades, molotovs) — editable directly instead of via the bandolier system. |
| `28_pickups.xml` (under `world1`) | World pickup definitions — small ammo/explosive/fuel pickups and named unique pickups like the Golden AK47. |
| `curves.xml` | Named curves referenced by value elsewhere (e.g. `Curves.PlayerSicknessCurves.MalariaTimeBeforeFirstAttack`, `Curves.Locomotion.Sprint`) — malaria timers, stamina/sprint curves, and max health. |
| `world.fat\domino\system\reinforcementregion.lua`, `spawnreinforcement.lua` | Real Lua scripts controlling checkpoint/guard-post reinforcement spawning — see [Gotchas](./gotchas.md) for whether patched Lua is honored at runtime. |
| `worlds.fat\worlds\world1\generated\world1.mapdata.fcb` | Patrol vehicle routes, raw XYZ coordinates with no in-game reference frame — editing routes is "hours of trial and error" without dedicated tooling. |
| `10_Ghostpatrols.xml` | Every patrol type's faction-color assignment plus optional vehicle passenger slots — see [Data Recipes](./data-recipes.md)'s faction-infighting recipe. |
| `*.sdat` (per-world-sector terrain) | See the [`.sdat` format page](../file-formats/sdat.md). |
| Vehicle files (`Vehicles_world1.xml`) | Vehicle model/mounting data. The hang glider's flight parameters were never found despite extensive searching. A `Chassis`-section `fHealth` value exists for ground vehicles, but recompiling a patch that changes it reliably crashes the game — unresolved, suspected DLC-folder conflict. |

## Locally preserved reference files (`research/reference-files/`)

Primary-source material mined from Discord exports, worth keeping independently of the (disposable,
since-deleted) raw export:

- **`tools/third-party/FC2Editor_Source/`** — genuine, working C# source for the stock map editor
  (422 entries, half a byte-identical `Backup/` duplicate). Built on a custom "Nomad" engine layer
  (`FC2Editor.Nomad`: `Camera`, `Engine`, `Render`, `EditorObject`/`EditorObjectSelection`,
  `Gizmo`/`GizmoHelper`, `TerrainManager`/`TerrainManipulator`, `TextureManipulator`,
  `SplineManager`/`SplineRoad`/`SplineZone`, `UndoManager`, `Validation`,
  `CollectionManager`/`CollectionManipulator`) plus a typed `FC2Editor.Parameters` UI-binding framework
  (`ParamBool`, `ParamFloat`, `ParamEnum<T>`, `ParamButton`, `ParamPickButton`). Confirmed genuine (not
  a community equivalent) via `AssemblyInfo.cs`'s Ubisoft 2008 copyright and its ~338 `Dunia.dll`
  P/Invoke declarations matching named exports in the Ghidra project — see [the editor API
  surface](../engine-internals/editor-api-surface.md), built directly from this source.
- **`hash-lists/`** — community-maintained CRC32→filename lookup lists: `worlds.filelist` (+ two
  earlier revisions), `worlds_english.filelist`, `entitylibrary.filelist`, `dlc1.filelist`,
  `dlc_jungle.filelist`, `patch.filelist`, `map_files.filelist`, and two early general-purpose
  `master_file_v1/v2.list` files.
- **`format-samples/`** — real, working instances of documented formats: `ak47.xbg`/`computer.xbg`
  (mesh), 4× `.xbt` texture samples, decoded FCB→XML data (`25_buddies.xml`/`world1.xml`/2×
  `soundpoint.xml`), real engine Lua scripts (`master_world1.world1.lua`/`master_world2.world2.lua`/
  `common_hq_doorman...lua`), a real archive pair (`patch.dat`+`patch.fat`), `movemgrnamed.bin`, a
  navmesh (`nv_4979.nvm`), a `.sbao` sample, a worked prefab-manager editing example, and a worked
  texture-swap example.
- **`tool-archives/`** — small tools shared as direct Discord attachments rather than on GitHub:
  `EXTRACTOR.7z` (fdx4061's `.xbg` texture/material extractor), `XBT-Thumbnail-Provider.zip`,
  `xbmEditor1.7z` (fdx4061's XBM editor), `SkeleTree.zip` (fdx4061's cross-game skeleton reading tool).
- **`text-snippets/`** — `CRC32_collisions.txt`, `Far_Cry_2_1.03_filediffs.txt` (the retail/GOG
  patch-gap root cause), `crc32_collision_examples.txt`, `fcbconverter_collision_repro.txt`,
  `redux_readme.txt`, `dunia_internal_tool_plugins_list.txt` (a leaked internal Dunia build-tool
  plugin list — ~50 build-pipeline plugins, confirming FC2's `.nvm` navmesh files are built on the
  open-source **Recast** navmesh library, and that `NomadDB` ties directly to the `FC2Editor.Nomad`
  namespace above), `cloth_shader_disasm_example.txt` (a readable disassembled `ps_4_0` HLSL pixel
  shader — shader bytecode is not opaque-packed binary).
