# JackAll

Purpose: Browse, edit and rebuild Far Cry 2's archives into a patch.dat the stock game loads; the home of every Dunia format, as app and CLI.
Stage: active

## Features

- [done] Mods tab - ordered mod list, workspace pinned last; Deploy and Revert always rebuild from patch.dat.vanilla
- [done] Fragments and three-way tree merge across mods - .fcb, MOVE, depload, strings, world descriptors, controls, .mgb, brains, fonts
- [done] _layout.xml moves entities between mission layers or deletes them - unchecked in game
- [done] Legacy mod import, plus legacy analyze/changes/check/pick to layer only the features picked
- [done] Files tab - the 13 archives merged as the engine resolves them, mod highlights, diff, replace/revert, unnamed files under _unknown\
- [done] References panel and xref build/to/from/reach - unused files can be hidden
- [done] File picker over the merged archives for any path, hash or sound field
- [done] Value editor for .fcb - one file per entity, component properties and enums from the engine's schema
- [done] Archetypes tab - library override chain, mods editing each archetype, dead-edit check (mod lint)
- [done] Saves tab - .sav metadata and progress, PersistenceDB editing, sav clean
- [done] Map tab scene editor - select, move, rotate, place, links, prefabs, Check, undo - nothing seen in game
- [done] Map tab characters wear their own outfit - user: "verified working"
- [done] Map tab performance - sector-filed rebuilds, VRAM 913 to 489 MB, grass within 100 m; user: "working"
- [done] Map tab frame-cost readout and crash.log - unreleased
- [done] Map tab drawn with Direct3D 11 in a native child window, no OpenGL or GLWpfControl, so it runs on every Windows 10 PC #66 - unreleased; user: "map tab is validated"
- [done] gamemodesconfig.xml merged block by block across layers, not last-one-wins; the AI tab saves the behaviour odds as their block - user 2026-11-12, unplayed
- [done] Particle libraries merged system by system across layers - for Aiming Overhaul's flashes beside Combat Overhaul's blood, unplayed
- [done] Pawn state machines, *.gosm.xml, merged group by group and state by state across layers - for UFCP's weapon switch (#81); tests pass, deployed to a game copy with UFCP's fragment and seen in game by fc2drive
- [done] New worlds - New map / world new and world pack - unseen in game
- [done] Terrain plans for new worlds, ridges raised by world pack - unreleased
- [done] Vegetation plans for new worlds, grass and RealTrees - unreleased; no fire data, not editable on the Map tab
- [done] Navmesh build with Recast from terrain and placed collision - nothing seen in game
- [done] AI tab - archetype tuning, optional-behaviour odds, brain behaviour tree; ai pack/unpack/fragments
- [done] Animations tab - MOVE graph as rules, move CLI and move clone (a clone played in game), .mab stick-figure player
- [done] Domino viewer and domino check
- [postponed] Domino editing (structural edits, codegen) and entity ids resolved to names - deferred by the user
- [done] .mgb package editor and per-area fragments - fragments work in game; the App's fragment save never run in the UI
- [done] HKX editor and hkx CLI for Havok collision - edited collision never checked in game
- [done] Material editor for .xbm, New material, xbm CLI - a material at a new path untested in game
- [done] Make a font, .mft viewer and mft CLI
- [done] .spk sound bank editor and XML round trip, Vorbis and IMA-ADPCM import
- [done] Sound labels for every retail .spk - unreleased
- [done] Asset tags in the Files tab and file picker - tag: search over every model, texture, sound and shader, one label for each - unreleased; user 2026-11-16: "it works well"
- [done] Shader labels and shader extract/index/build/label - unreleased
- [done] .xbt, .sbao, .xbg and .rtx viewers and converters - bundled ffmpeg for audio, bare .dds builds
- [done] .fc2model export and apply - actor, animation tag windows, shader property lists
- [done] Dark theme
- [done] jackall-cli with --json, and jackall-mi for the Vortex extension
- [done] mod build stops on two installed sound banks with different records under one id; jackall-mi only warns - unreleased; stopped on Vehicle and Inventory Overhaul's 0x00FD0001-0x00FD0004
- [done] Test fixtures restored from a Steam install and two Nexus mods by manifest.tsv - fixtures restore/record, a Stop hook for unlisted files
- [done] Stop on conflicts, a Mods-tab box, off by default: off, the lower mod wins a value two mods set differently, as in jackall-mi and the CLI; on, Deploy stops there; the conflicts are listed either way, as selectable text in a window with only OK - user: "I tested and approve the light theme of the JackAll conflict window"
- [postponed] Tutorial docs and screenshot scripts - run-all not re-run since the UI-script refactor; verify-in-game.ps1 never deployed; postponed by the user past 1.8.0
- [scrapped] Map editor on FC2Editor/FCE over host IPC - user: the wrong direction; the AVATAR port (18e58eab) is a quarry only
- [scrapped] Map tab per-sector view culling - culled almost nothing, refilled streams on turns (d8b0f884)
- [scrapped] Map tab Demo-mode speed-ups - user: a non-issue
- [scrapped] SoundBench as a JackAll tab - SoundBench stays standalone

## To validate

- Deploy twice with JackAll open: both build, and Data_Win32 holds no `*.old` file; Windows 10 1809 itself only the Nexus reporter can confirm (bug 1172349) - tests only

## Approvals

- played: d04a2176 2026-11-18 - "I read all texts and played all mods and validated all lines"
- page text: 297912af 2026-11-16 - BBCode and short message checked: "page text is validated"

## Checks

- simplify: 3197dd65 2026-11-21 - since jackall-1.8.0; the gosm splitter repeats the input-config and game-modes splitters' shape, a shared base left for after the release

## Release order

## Collisions
