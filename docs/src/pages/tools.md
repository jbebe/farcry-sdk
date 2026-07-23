---
title: Tools
description: Custom-built and third-party tools used across this project, with links to source.
---

# Tools

An inventory of everything under `tools/` in the repository — the mod manager and utilities built
for this project, plus every third-party tool kept around for reference or as a dependency.

## Built for this project

- **[JackAll](https://github.com/jbebe/farcry-sdk/tree/main/tools/JackAll)** — a full mod manager
  for Far Cry 2. Presents every game archive as one browsable filesystem, lets you stage edits or
  drop in existing community mod zips, and compiles the result into a real `patch.dat`/`patch.fat`
  that the stock engine loads directly — no DLL injection, no modified executable. Builds are
  reproducible (rebuilding twice yields byte-identical output) and tested against the real shipped
  archives rather than fixtures. See [.fat/.dat — Archive Loading](/docs/file-formats/archives-fat-dat)
  for the format background.
- **[compact_export.py](https://github.com/jbebe/farcry-sdk/tree/main/tools/misc/DiscordChatExporter)**
  — post-processes raw JSON exports from the third-party [DiscordChatExporter](https://github.com/Tyrrrz/DiscordChatExporter)
  into a compact form usable for research, so full modding-Discord history can be searched/grepped
  locally.
- **`sbao_tool.py`** (`tools/misc/sbao/`) — an early proof-of-concept `.sbao` sound-format decoder.
  Superseded — its logic was folded into JackAll's built-in `.sbao` handler.
- **ModPatcher** (`tools/misc/modpatcher/`) — a proof-of-concept `dinput8.dll` proxy that hooks
  `Dunia.dll`'s `VFS_ResolvePath` to redirect asset loading to a loose `Data_Win32\Loose\` folder
  (Skyrim/STALKER-style loose-file overrides), instead of repacking `.fat`/`.dat` archives. Worked
  and was dynamically verified end-to-end, but JackAll's `patch.dat`-based approach was chosen
  instead for shareability — kept for reference. See its
  [README](https://github.com/jbebe/farcry-sdk/tree/main/tools/misc/modpatcher) for the full
  DLL-proxy/inline-hook writeup.

## Third-party

Kept locally under `tools/third-party/` for reference and testing; most of these binaries are
git-ignored rather than committed (too large / redistribution-unclear), so only the folder
structure and any `README`/notes are tracked in the repo unless noted otherwise.

| Tool | What it does | Source |
|---|---|---|
| **Gibbed.Dunia** | The canonical open-source Dunia-engine toolset (C#) — unpack/pack archives, convert binary object trees to/from XML. The base most other community tools wrap. Vendored as a git submodule here. | [github.com/gibbed/Gibbed.Dunia](https://github.com/gibbed/Gibbed.Dunia) |
| **WobFC2Dunia042 (Wobatt's Hash Decoder)** | A modified Gibbed toolset + XML/hash decoder resolving far more hash-only filenames than stock Gibbed tools (97% vs. 55% of files identified). | [OWG forum thread](https://www.openworldgames.org/owg/forums/index.php/topic,3633.0.html) |
| **FC2_DuniaTools** | A prebuilt Gibbed.Dunia binary drop (`Bootstrap`, `ArchiveViewer`, `ConvertBinary`, `ConvertXml`, `Pack`, `Unpack`) — ready-to-run copy rather than source. | via Gibbed's toolset above |
| **DuniaTools** | Standalone GUI extractor/converter for Far Cry 2 archive files. | [Nexus Mods](https://www.nexusmods.com/farcryprimal/mods/5) |
| **RunGUI** | GUI wrapper around a pinned Gibbed.Dunia SVN build (r179) adding XBT↔DDS image conversion and custom `.fat`/`.dat` unpacking. | based on Gibbed's SVN builds (`svn.gib.me`) |
| **FCBConverter** | Unpacks/packs `.fat`/`.dat` archives and converts the FCB binary object format to/from an editable XML representation. Built mainly for later Far Cry titles but usable on FC2. | [downloads.fcmodding.com](https://downloads.fcmodding.com/others/fcbconverter/) |
| **Ubitunedec / DecUbiSnd** | Decodes and exports `.spk`/sound data embedded in Ubisoft `.dat` archives (character voices, music, dialogue). | [github.com/beawy/Ubitunedec](https://github.com/beawy/Ubitunedec) |
| **Dunia-Engine-XBG-Blender-Importer** | Blender add-on importing/editing/re-exporting `.xbg` 3D models across ten Ubisoft titles including FC2, grown from an Avatar-only importer. Currently the most active lead for custom mesh modding. | [github.com/Quiet-Joker/Dunia-Engine-XBG-Blender-Importer](https://github.com/Quiet-Joker/Dunia-Engine-XBG-Blender-Importer) |
| **Material and Texture Extractor** | Extracts textures/materials referenced by `.xbg` mesh files into folders mirroring the original resource directory structure, driven by a `PATH.INI`. | shared directly in the FC2 modding Discord (not on GitHub) |
| **xbmEditor1** | Double-click editor for `.xbm` material files. | shared directly in the FC2 modding Discord (not on GitHub) |
| **SkeleTree** | Cross-game skeleton/rig reader, confirmed working on Avatar (2009), FC2, and FC3. | shared directly in Discord (not on GitHub) — the one third-party binary actually committed here |
| **FC2Editor_Source** | Archived C# WinForms source for the original `FC2Editor` map editor, built on a custom "Nomad" engine layer. Recovered via the Wayback Machine after the original CodePlex project went offline. | [archived CodePlex project](https://web.archive.org/web/20190809183526/https://archive.codeplex.com/?p=fc2editor) |
| **Universal Patcher v1.1** | Batch-driven mod installer: unpacks a "master" `patch.fat`/`patch.dat` with Gibbed tools, overlays one or more mod folders on top, repacks. Lets less technical users combine mods without learning the toolchain directly. | community batch script (author: Skibbo), no public repo found |
| **Far Cry 2 Dedicated Server (debug)** | Debug build of the Linux dedicated server binary, kept for reverse-engineering cross-reference against `Dunia.dll`. | community-shared debug build, not publicly linked |
| **Far Cry 2 Xbox (debug)** | Debug/dev Xbox 360 build (`.xex`/`.xdb` symbols, `.nfo` manifests) — source of the Domino Lua mission-scripting system and QA cheat-script findings documented in [Engine Internals](/docs/category/engine-internals). | community-shared debug build, not publicly linked |

For the full research trail behind this list — forum threads, Discord provenance, and tools not
yet pulled into the repo — see [Sources](/docs/modding/sources#tools-beyond-whats-already-in-the-mods-survey).
