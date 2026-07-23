---
sidebar_position: 8
---

# Install File Manifest

:::note[Community-reported, with some reverse-engineered corrections]
Built from a live directory listing, not from documentation alone — sizes and paths below were
measured directly. See [Getting Started](./getting-started.md) for the full provenance note.
:::

A categorized survey of every file group in the Steam install
(`C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\`). Status reflects tooling maturity, not
independent re-verification of every single file.

**Status key**: **Tooled** (round-trips cleanly with existing community tools) · **Partial** (some tooling exists, real gaps remain) · **Locked** (no known extraction/edit path beyond raw hex editing) · **Out of scope** (third-party/vendor content, not FC2-specific).

Total data footprint: ~3.3 GB, dominated by `worlds.dat` (2.5 GB) — that single archive holds the bulk of moddable content (meshes, textures, terrain, entity data), not the more obviously-named `common` archive.

## 1. Packed Data Archives (.fat/.dat) — Tooled

The engine's container format: a `.fat` index paired with a `.dat` blob. Everything else in this manifest (except loose executables/config) lives inside one of these seven pairs. Filenames inside are CRC32 hashes, not stored as strings — resolving them needs a community filelist (`research/reference-files/hash-lists/`). See also [the archives format page](../file-formats/archives-fat-dat.md) for how the engine resolves these at runtime.

| Path | Size |
|---|---|
| `Data_Win32/common.dat` + `.fat` | 100 MB |
| `Data_Win32/patch.dat` + `.fat` | 9.8 MB |
| `Data_Win32/shadersobj.dat` + `.fat` | 250 MB |
| `Data_Win32/sound.dat` + `.fat` | 156 MB |
| `Data_Win32/sound_english.dat` + `.fat` | 118 MB |
| `Data_Win32/worlds/worlds.dat` + `.fat` | 2.5 GB |
| `Data_Win32/worlds/worlds_english.dat` + `.fat` | 109 MB |
| `Data_Win32/downloadcontent/dlc1/{dlc1,dominos,entitylibrary,menus}.dat/.fat` | — |
| `Data_Win32/downloadcontent/dlc_jungle/{dlc_jungle,menus}.dat/.fat` | — |

**Tools**: `Gibbed.Dunia.ArchiveViewer`/`Unpack`/`Pack`, wobatt's improved decoder, FCBConverter. `patch.fat` is the one every mod actually targets — it carries `entitylibrarypatchoverride.fcb`, the official override hook (see [Getting Started](./getting-started.md)).

## 2. Entity / Object Binary Data (.fcb) — Tooled

Inside the archives above: compiled object-definition trees — weapons, vehicles, player/buddy stats, gamemode config, world sectors. Round-trips to human-editable XML. See [the `.fcb` format page](../file-formats/fcb.md) for the confirmed byte layout.

- `patch.fat/generated/entitylibrarypatchoverride.fcb`
- `worlds.fat/world{1,2}/generated/entitylibrary{,_full}.fcb`
- `worlds.fat/…/worldsectors/worldsectorNN.data.fcb`
- `worlds.fat/…/world1.mapdata.fcb`
- `libraries/world1/{41_WeaponProperties,42_weapons,30_player}.xml.fcb`

**Tools**: `Gibbed.Dunia.ConvertBinary` (fcb ⇄ xml). **Exception — Locked**: `downloadcontent/dlc1/entitylibrary.fcb` (DLC weapon data) cannot be decompiled by ConvertBinary at all; the only known path in is a raw hex editor (see [Getting Started](./getting-started.md) and [Data Recipes](./data-recipes.md)).

`enemy_archetypes.xml` is confirmed community-established as the AI-tuning file for things like enemy FOV/view distance/cover occlusion (Discord, `🔨-fc3-and-bd-modding`, 2025-11-27) — referenced by a user asking for its FC3 equivalent, unanswered in-thread, but implying the filename is common knowledge for anyone tuning FC2 enemy perception.

A related but distinct binary format, `depload.dat` (a dependency/"parents" chunk, not an object tree — not decodable by `ConvertBinary` at all), is not FCBConverter-supported and has its own [format page](../file-formats/depload.md).

## 3. Meshes & Materials (.xbg / .xbm) — Partial

3D geometry (`.xbg`) and material/texture-reference definitions (`.xbm`) — structurally the same reversed-FourCC chunk format under two extensions. Lives in `worlds.fat/graphics`.

- `worlds.fat/graphics/**/*.xbg` — meshes, incl. character/buddy models
- `worlds.fat/graphics/**/*.xbm` — materials

Byte-level format documented as of 2026 (section order, alignment rule, bone-palette mechanism) — see [the `.xbm`/`.xbg` format page](../file-formats/xbm-xbg.md). `Dunia-Engine-XBG-Blender-Importer` gives a real import/export path, but is pre-alpha — weapon-xbg import is currently broken, HKX collision export doesn't work yet. Texture-only reskins (no mesh edits) have been standard practice since 2011.

## 4. Textures (.xbt) — Partial

Compressed texture assets referenced by `.xbm` materials, packed inside the same archives (`worlds.fat/**/*.xbt`).

`.xbt → .dds` extraction is solid and has been for years (010 Editor templates, `xbt2dds`). Repacking `.dds → .xbt` was historically the shakier direction but is implicitly solved in practice — the community has shipped full weapon reskins since 2011–2012.

## 5. Terrain / Heightmaps (.sdat) — Tooled

Per-sector heightmap + auxiliary terrain data (`worlds.fat/…/worldsectors/*.sdat`). Each multiplayer map is 512m×512m, divided into an 8×8 grid of sectors.

:::info[Verified via reverse engineering]
The community's own byte-layout guess for these files was wrong — see [the `.sdat` format
page](../file-formats/sdat.md) for the confirmed real layout (a generic chunked container, not a
bare heightmap array).
:::

A Blender plugin can reportedly read *and* edit these directly. Whether the 8×8 sector grid itself can be enlarged is unresolved.

## 6. Navigation Mesh (.nvm) — Locked

AI pathfinding navmesh data, compiled from level geometry (`worlds.fat/**/*.nvm`).

Confirmed built on the open-source **Recast** library (via a leaked internal build-tool plugin list — `RecastNavmeshCompiler`/`Exporter`), but no FC2-specific decode/edit tool has surfaced in this research.

## 7. Audio — Partial

Voice, SFX, and ambient sound — both a top-level archive pair and packed sound objects inside `worlds.fat`.

- `Data_Win32/sound.dat` + `.fat` (156 MB), `Data_Win32/sound_english.dat` + `.fat` (118 MB)
- `Data_Win32/SoundBinary/DARE.INI` — audio middleware config
- `worlds.fat/**/*.spk` — sound bank objects, hash-named
- `worlds.fat/**/*.sbao` — soundbinary objects
- `scripts/game/barkdata/*.bank`

Active community `.spk` editing exists ("enough to mod them, but not everything" — Gabor). `DARE.INI` pairs with `bin/eax.dll` (Creative EAX) but hasn't been deeply researched here.

:::info[Verified via reverse engineering]
`.spk`'s container format is now reverse-engineered and tooled — see [the `.spk` format
page](../file-formats/spk.md): magic `0x53504B01`, a record count, an id table, then
variable-length `{preamble, size, payload}` records, 4-byte aligned. Verified byte-for-byte against
every `.spk` in a real install (8,282 files, 42,215 records, zero failures) and implemented in
`tools/JackAll/src/JackAll.Core/Format/SpkPackage.cs` / `JackAll.App`'s `SpkFileHandler`. This
matches Gabor's "enough to mod them, but not everything": each record's own payload is registered
by the engine as an opaque `{id, pointer, size}` triple at load time and only interpreted later, at
actual playback — see [the `.sbao` format page](../file-formats/sbao.md) for the standalone-file
sibling format.
:::

## 8. Video (Bink) — Out of scope

Cutscenes and intro/outro movies, RAD Game Tools' Bink format.

- `Data_Win32/ui/video/ending.bik`
- `bin/binkw32.dll` — Bink codec

Standard, well-documented third-party format with its own public tooling ecosystem — not FC2-specific, so not covered by the modding research.

## 9. Lua Scripts — Partial

Real embedded Lua, packed inside `worlds.fat`'s "domino" AI subsystem — confirms Lua exists in FC2 for at least reinforcement/interaction logic.

- `worlds.fat/domino/system/reinforcementregion.lua`
- `worlds.fat/domino/system/spawnreinforcement.lua`
- `worlds.fat/…/master_world{1,2}.world{1,2}.lua`
- `worlds.fat/…/common_hq_doorman*.lua`

Whether a *patched* Lua file is honored at runtime is contested — see [Gotchas](./gotchas.md) for
the conflicting reports (one 2011 report says the game silently reads the original; a 2016 report
and a 2022 outpost-respawn-timer mod both confirm patched Lua working). Treat as
subsystem-dependent, not uniformly reliable.

## 10. Menu/UI Resources (.mgb / .mgb.desc) — Partial

Compiled menu/UI layout data ("Magma" — the engine's own internal name for this subsystem, confirmed
via `CMagmaUIResource`/`CMagmaConfigUIResource` class names and the `magma::` C++ namespace). Lives in
`ui\localized\{pc,pcwidescreen}\<lang>\ui\*.mgb[.desc]`, packed inside `patch.fat` (full localized set,
smallest archive to pull samples from) and also present in `common.fat`/`worlds.fat`.

- `.mgb.desc` — **plain, well-formed XML** (nav-bar button-prompt text bindings + a `<dependencies>`
  tree naming other required `.mgb.desc`/`.mgb`/`.xbt` resources). Directly Notepad++-editable — no
  hex editor needed. This corrects the [Almost Complete Guide](./guide/file-management.md)'s claim
  that both files "can only be edited with a hex editor"; that's only true of the binary half.
  **JackAll** shows it as plain, syntax-highlighted XML (routed through the same viewer as `.xml`).
- `.mgb` — binary, `"MAGMA"`-magic (5-byte ASCII, not a reversed-FourCC like `.xbm`/`.xbg`). Contains
  widget geometry/alpha floats, UTF-16LE UI strings, and an animation-keyframe layer, all reversed to
  full byte precision. **Read-only, not round-trip yet**: a real file's class-name coverage is
  incomplete (42/166 type-table entries resolved in one sample), so the decoder stops cleanly at the
  first unrecognized widget class rather than guessing — full files don't decode end-to-end yet.

**Tools**: **JackAll** has a real (partial) decoder and tree viewer for `.mgb`
(`JackAll.Core.Format.Mgb*`) — the first published tooling for this binary format's contents, not just
its container. `Gibbed.Dunia.Unpack.exe` still handles extraction (round-trips the container, not the
`.mgb` binary itself). Byte-level RE notes, confirmed class hierarchy, and load-flow tracing (via the
`FarCry2_server` binary) are on [the `.mgb` format page](../file-formats/mgb.md).

## 11. DLC Manifests (.rml) — Tooled

Table-of-contents / resource-manifest XML variant listing a DLC package's contents.

- `Data_Win32/downloadcontent/dlc1/toc.rml`
- `Data_Win32/downloadcontent/dlc_jungle/toc.rml`

**Tool**: `Gibbed.Dunia.ConvertXml.exe` handles `.rml` ⇄ XML directly.

## 12. Engine & Game Executables — Locked

Compiled binaries — the game itself, its engine core, and supporting native/managed libraries. All live in `bin/`.

- `FarCry2.exe` — main game binary
- `Dunia.dll` — engine core, confirmed hex-patchable
- `FC2.dll`
- `FC2Editor.exe` — map editor; source recovered, see `research/reference-files/fc2editor-source/`
- `FC2Launcher.exe`, `FC2ServerLauncher.exe`
- `FC2BenchmarkTool.exe` + `.config`
- `Microsoft.DirectX{,.Direct3D,.Direct3DX}.dll` — legacy MDX wrapper
- `SandBar.dll`, `SandDock.dll` — WinForms docking UI (editor); safe to delete per community report
- `gpudatabase.dll` + `pcidevs.txt`, `extendedpcidevs.txt`
- `systemdetection.dll` — splash-hang fix: swap for FC3's copy
- `FC2Init.ini`, `lang.ini`

Not just opaque binaries — **Dunia.dll** has two confirmed community hex-patches (a weapon-icon fallback fix, and the "Multi editor mod"'s modified DLL for extra editor functionality) — see [Data Recipes](./data-recipes.md) and [Engine Theory](./engine-theory.md). **FC2Editor.exe**'s likely source is preserved at `research/reference-files/fc2editor-source/` (splash-hang fix in [Gotchas](./gotchas.md)).

## 13. Anti-Cheat (PunkBuster) — Out of scope

Third-party anti-cheat, bundled and self-updating — unrelated to game content.

- `bin/pb/dll/*.dll`, `bin/pb/htm/*.htm`
- `bin/pb/{pbag,pbags,pbcl,pbcls,pbsv}.dll`, `pbns.dat`
- `installers/PunkBuster/pbsvc.exe`
- `revoke.bat` (root) — PB revocation script

Irrelevant to modding; only matters if multiplayer/PB-enforced servers are in play.

## 14. Redistributable Installers — Out of scope

Vendor prerequisite installers, bundled for a fresh machine.

- `installers/DirectX/*.cab`, `DXSETUP.exe`, `DSETUP.dll`
- `installers/DotNetRedist/{dotnetfx,NetFx64}.exe`
- `installers/VCRedist/vcredist_x86.exe`

Off-the-shelf Microsoft/legacy redistributables — no FC2-specific content.

## 15. Localization Resources — Out of scope

Per-language UI resource folders for the launcher/benchmark tooling (distinct from in-game text, which lives inside `sound_english.*`/worlds data as subtitle strings).

- `bin/Resources/{cs,de,es,fr,it,nl,pl,ru,uk,us}/`

Not investigated in the modding research — likely satellite `.resources.dll`-style assemblies for the .NET launcher/benchmark tools, not gameplay-relevant.

## 16. Top-Level Docs & Install Metadata — Out of scope

Root-level housekeeping files, not part of the runtime data pipeline.

- `ReadMe.txt`, `PatchNotes.txt`, `manual.pdf`
- `19900_install.vdf`, `21960_install.vdf` — Steam depot manifests

Reference-only; safe to ignore for modding purposes.
