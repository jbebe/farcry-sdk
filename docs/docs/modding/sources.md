---
sidebar_position: 8
---

# Sources

:::note[Community-reported]
See [Getting Started](./getting-started.md) for the full provenance note.
:::

A catalog of where the community keeps its actual technical knowledge — forums, tools, and
file-format research — as distinct from [Mods Survey](./mods-survey.md), which lists individual mods.

## Forums & discussion sites

- **[OpenWorldGames (OWG) — "Single player modding" board](https://www.openworldgames.org/owg/forums/index.php?board=169.0)**
  — the richest technical source for FC2 modding, ~81 threads (2011–2017, still occasionally active).
  Gibbed himself posted his tool announcement and workflow directly here ([topic
  2390](https://www.openworldgames.org/owg/forums/index.php/topic,2390.0.html)), and the forum has
  hosted his tools ever since. wobatt (author of an improved XML/hash decoder) is also a regular
  poster. The technical distillation of this forum is [Getting Started](./getting-started.md) and its
  sibling pages.
- **[guru3D — "Far Cry 2 Mod info and help thread"](https://forums.guru3d.com/threads/far-cry-2-mod-info-and-help-thread.395033/)**
  — short (8 posts, 2014), mostly a funnel into OWG, plus one user's working example patch. Real new
  content folded into [Getting Started](./getting-started.md)/[Data Recipes](./data-recipes.md): the
  full per-biome FOV field structure, the full detection-duration field list, and the
  `gamemodesconfig.xml`-in-two-archives report now folded into [Gotchas](./gotchas.md).
- **[guru3D — general FC2 release thread](https://forums.guru3d.com/threads/the-guru3d-far-cry-2-thread.276864/)**
  — general release discussion (reviews, DRM gripes, DX9/DX10 comparisons), not modding-technical.
  One useful fact: patch 1.3 removes the SecuROM DRM.
- **[Far Cry Wiki (Fandom)](https://farcry.fandom.com/)** — has dedicated Map Editor and FCBConverter
  pages, cross-reference material.
- **["An Almost Complete Guide to Far Cry 2 Modding"](./guide)** (Boggalog) — the community's closest
  thing to an SDK/reference manual, covering packing/unpacking, XML editing/decoding, hex editing,
  texture conversion, and `.fat`/`.dat` handling. Saved locally in full; treat it as a standing
  reference to search directly for specific questions rather than something summarized here.

## File-format reverse-engineering references

- **[ZenHAX — "Ubisoft file format (FAT2/FAT3)"](https://www.zenhax.com/viewtopic.php@t=11.html)** and
  **["Far Cry 4 'Dunia' .fat/.dat archives"](https://www.zenhax.com/viewtopic.php@t=378.html)** — the
  original source for the FAT-version lineage: FC2 uses `-v5`, FC3/4 use `-v9`, FC5 uses `-v10` (later
  independently corroborated via fcmodding.com's own FCBConverter docs).
- **[XeNTaX — "Far Cry 3 .FAT decryption"](https://forum.xentax.com/viewtopic.php?f=10&t=9927)** and
  **["Far Cry 5 .Fat and .Dat Files"](https://forum.xentax.com/viewtopic.php?f=10&t=17888)** —
  later-game format work, useful for cross-referencing container structure even though FC2's version
  differs.
- **[QuickBMS](https://aluigi.altervista.org/quickbms.htm)** — universal script-driven extractor;
  likely has an existing FC2-compatible BMS script worth checking before writing a custom unpacker.

## Dedicated modding sites

- **[fcmodding.com](https://fcmodding.com/)** — primarily an FC3–FC6 hub, not FC2-focused, but its
  [FCBConverter](https://downloads.fcmodding.com/others/fcbconverter/) documentation page is a useful
  secondary confirmation of FAT versioning and the complete XML field-type system (`Int`/`UInt`/
  `Float`/`Vector`/`String`/`Enum`/`Hash`/`Id`/`Boolean`/`BinHex`) used for hand-editing converted
  values — see [Getting Started](./getting-started.md).
- **[fc2mp.com](https://fc2mp.com/)** — community site reviving/maintaining FC2 online multiplayer.
  Its [Downloads](https://www.fc2mp.com/Downloads/) page hosts real map-editor mods (see [Mods
  Survey](./mods-survey.md)) and links ModDB's ["Far Cry 2 Map Editor Run
  Through"](https://www.moddb.com/games/far-cry-2/tutorials/far-cry-2-map-editor-run-through) tutorial
  — the canonical beginner guide, distilled into [Engine Theory](./engine-theory.md)'s Map Editor
  section.
- **[GameBanana — Far Cry 2 Hub](https://gamebanana.com/games/983)** — mods, tutorials, Q&A. Its
  tutorials contributed the exact `FC2Editor.exe` launch paths and the
  `Documents\My Games\FarCry2\usermaps\` install path (folded into [Engine
  Theory](./engine-theory.md)).

## Tools

- **[Gibbed.Dunia](https://github.com/gibbed/Gibbed.Dunia)** — the canonical open-source toolset for
  Dunia-engine games, written in C#. Contributors include Gibbed himself and **Janne252** (also the
  author of "Janne252's editor mod" on fc2mp.com). The base that SCHTEVE, the trainer, and most
  community tools wrap.
- **[FCBConverter](https://downloads.fcmodding.com/others/fcbconverter/)** — unpacks/packs
  `.dat`/`.fat` and converts the FCB binary format to/from editable form; built mainly for FC5/New
  Dawn but usable for older games. Author: **ArmanIII**.
- **[FarCry2-Schteve](https://github.com/tylerdotrar/FarCry2-Schteve)** ("SCHTEVE") — wraps
  Gibbed.Dunia (unmodified) + `xbt2dds` (lightly modified) + a "FC2 XML Decoder" behind a PowerShell
  TUI, with a config-driven "Sandbox" working directory model. Author: **Steve64b**.
- **[FarCry2_trainer](https://github.com/GregoryKimball/FarCry2_trainer)** — WPF app for
  tweaking/modding FC2, built on Gibbed's binaries. Feature set (unlock all weapons/equipment,
  manuals cost 1, remove malaria, unlimited sprint, golden AK-47 respawn, max slope climb cap, weapon
  slot reassignment, silencing a weapon, removing accuracy spread) is consistent with the recipes in
  [Data Recipes](./data-recipes.md).
- **[Queiroz-Far-Cry-2-Modding-Tool](https://github.com/Hoklifter/Queiroz-Far-Cry-2-Modding-Tool)** —
  Python-based XML-modding facilitation tool, tested on Windows by **hoklifter**.
- **[Far-Cry-2-Multi-Fixer](https://github.com/FoxAhead/Far-Cry-2-Multi-Fixer)** — launches FC2 with
  various fixes without modifying game files; non-invasive alternative to FC2MPPatcher. Also fixes a
  framerate-cap bug that causes NPCs to visibly bounce.
- **[FC2MPPatcher](https://github.com/halvors/FC2MPPatcher)** — community multiplayer-revival
  patcher; the actual online service is a fan-run replacement backend at longweep.net. Fixes
  network-interface binding, LAN/VPN broadcast, and dedicated-server IP-announcement bugs.
- **[farcry2_sdk](https://github.com/tylerjharden/farcry2_sdk)** — a C#/.NET wrapper and SDK for
  interfacing with the FC2 game and editor on Windows, directly addressing the "no official SDK" gap.
- **[Dunia-Engine-XBG-Blender-Importer](https://github.com/Quiet-Joker/Dunia-Engine-XBG-Blender-Importer)**
  — the current, actively-developed answer to custom 3D model import/export for FC2, described in full
  on the [`.xbm`/`.xbg` format page](../file-formats/xbm-xbg.md): real (if pre-alpha/buggy) mesh import
  and export/injection, built for Avatar: The Game but working for FC2 given the shared engine
  lineage. Author: **Quiet_Joker**.
- **wobatt's "Far Cry 2 XML File Decoder"** (hosted on OWG) — sits on top of Gibbed's tools and
  resolves far more hash-only names than the original; see the comparison table in [Getting
  Started](./getting-started.md).
- **Archived FC2 Editor source code** — decompiled C# source for the actual stock map editor
  (`AssemblyCopyright("Copyright (C) 2008 Ubisoft Entertainment")`), preserved locally at
  `tools/third-party/FC2Editor_Source/`. See [Getting Started](./getting-started.md) and [the editor
  API surface page](../engine-internals/editor-api-surface.md), built directly from this source.
- **`SkeleTree`** (fdx4061) — cross-game skeleton reading tool, confirmed working on Avatar (2009),
  Far Cry 2, and Far Cry 3. Preserved locally at `research/reference-files/tool-archives/SkeleTree.zip`.
- **`EXTRACTOR.exe`** (fdx4061) — automatically extracts textures/materials referenced by `.xbg`
  files into organized subfolders. See [Getting Started](./getting-started.md) for usage.
- **`XBT-Thumbnail-Provider`** (fdx4061 + JasperZebra) — a Windows Explorer shell extension showing
  real `.xbt` texture thumbnails in Explorer's icon views.
- **`dunia_map_visualiser`** (Gabor) — renders FC2/Avatar world-sector heightmap data into a visual/3D
  map matching the real in-game layout; used to confirm the sector-grid numbering in [Getting
  Started](./getting-started.md).
- **[FARCRY_2_Diamond_Editor](https://github.com/JasperZebra/FARCRY_2_Diamond_Editor)** — a savegame
  diamond-count editor (see [Gotchas](./gotchas.md)'s diamond-count-instability note before using it
  to front-load a large amount).

## Discord servers

- **["Far Cry Modding Community"](https://discord.com/invite/farcry-modding-846424998888734731)** —
  general Far Cry series modding server, ~17,400 members, covers tutorials/tools across the whole
  series. Its `⭐ Far Cry 2 / 🔨-fc2-modding` channel is the single richest source in this whole
  project — FCBConverter's author, SCHTEVE's author, Redux's author, and the author of the tool that
  finally solved custom mesh import all post directly and in dialogue with each other there.
- **"Far Cry 2 Multiplayer"** (fc2mp.com's Discord) — 4 channels: `other-fc2-stuff`, `tools-and-mods`,
  `modding`, `map-editor`.

## Key community figures

- **gibbed** ("Rick") — original tool author, posted directly on OWG in 2011, personally clarified
  several format questions.
- **wobatt** — built the improved XML/hash decoder and a modified Gibbed toolset; documented the DLC
  entity-editing recipe.
- **stoatoats** — author of "RealMod" (ModDB); demonstrated direct hex-editing of DLC's
  `entitylibrary.fcb`.
- **Art Blade**, **PZ** — OWG admins, did much of the early (2011) file-format spelunking.
- **TheStranger**, **nexor**, **OWGKID**, **Knightmare** (found the magazine-capacity hash map and
  the ballistic spread-angle conversion formula), **TheFishlord** (author of the Realistic Weapons
  Pack mods and the `42_weapons.xml`-crash workaround), **shelmez** (weapon texture reskins),
  **Diablo_Lobo** (camo/detection tuning), **hans_dampf36** (Lua/checkpoint investigation, later a
  developer on "Infamous Fusion"), **Rhynder** (original full-auto AR-16 fix, early enemy-respawn Lua
  investigation), **Vaatho** (faction infighting, vehicle HP attempts), **LinkHero** (ammo pickup
  hash table, dart rifle fire-mode fix), **chiconspiracy** (hit-location/accuracy breakthrough) —
  recurring OWG technical contributors, 2011–2017.

**Currently active (2025–2026, "Far Cry 2 Multiplayer" Discord):**

- **Gabor** — the server's most technically deep active contributor. Owns/maintains a from-scratch
  `.xbm`↔XML and `.xbg`↔XML converter, a Dunia world-sector map visualiser, and cross-references
  extensively against Avatar: The Game. Also does active `.spk` sound-format modding.
- **fdx4061** — builds complementary tooling: a cross-game skeleton-file reader, an XBM editor, and
  the XBG texture/material extractor. Worked on cracking the character-XBG bone-palette mystery
  documented on the [`.xbm`/`.xbg` format page](../file-formats/xbm-xbg.md).
- **JasperZebra** — author of `FARCRY_2_Diamond_Editor`, `Borderless_Window_Maker`,
  `AVATAR-The-Game-Level-Editor`, and `XBT-Thumbnail-Provider`.

**Currently active (2021–2026, "Far Cry Modding Community" Discord, `🔨-fc2-modding` channel):**

- **Hunter** — the most prolific and knowledgeable contributor in this channel, and the actual author
  of "FC_Redux." Deep practical knowledge across weapon modding, buddy/character model swapping,
  `Dunia.dll` binary patching (the weapon-icon fix, see [Data Recipes](./data-recipes.md)), and
  general troubleshooting since 2021. First-hand source for the "buddies are a scripted facade"
  explanation.
- **ArmanIII** — author of FCBConverter; personally debugged and fixed live FC2-specific bugs in his
  own tool in this channel (see [Getting Started](./getting-started.md)). Also shipped a Far Cry 2
  Mod Installer.
- **Steve64b** — author of SCHTEVE; ArmanIII's regular collaborator/tester.
- **Boggalog** — author of ["An Almost Complete Guide to Far Cry 2 Modding"](./guide) and of a
  substantial overhaul mod; deep knowledge of the weapon-icon/`sName` system and holster mechanics.
- **scubrah**, **Lasercar**, **RaZoR-FIN** — scubrah built the outpost-respawn-timer POC and helped
  root-cause the GOG/DVD entitylibrary gap; Lasercar surfaced the archived FC2Editor source code and
  drove filelist-completion work, and does ongoing Blender/mesh investigation; RaZoR-FIN does
  coop/multiplayer experimentation and archetype-override research.
- **Quiet_Joker** — author of `Dunia-Engine-XBG-Blender-Importer`, the tool that resolved the
  character-mesh/bone-palette mystery in mid-2026.
- **thatdarnowl**, **MysteryPL**, **sharp_razor8** — active 2026 testers/collaborators on
  Quiet_Joker's importer, surfacing concrete bugs in real time.
- **bajuh** — independently reverse-engineering `Dunia.dll` with Ghidra, cross-referencing the Linux
  FC2 dedicated server binary (see [the engine overview](../engine-internals/overview.md)), aiming to
  build an FCB-editing tool.
- **Ganic** — active FC2→FC3 asset porter, hit the same `.xbm`/`.xbg` material/bone-weighting wall
  documented on the [`.xbm`/`.xbg` format page](../file-formats/xbm-xbg.md), from the opposite side.

## Locally preserved reference files

See [Getting Started § Locally preserved reference
files](./getting-started.md#locally-preserved-reference-files-researchreference-files) for the full
breakdown of `research/reference-files/`.
