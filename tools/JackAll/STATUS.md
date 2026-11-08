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
- [in progress] Map tab characters wear their own outfit - a placed merc draws as a marker; in-app check outstanding
- [in progress] Map tab performance - sector-filed rebuilds, VRAM 913 to 489 MB; grass distance pick pending, eviction unseen by eye
- [done] Map tab frame-cost readout and crash.log - unreleased
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
- [done] Sound labels for every retail .spk, spk: search - unreleased
- [done] Shader labels and shader extract/index/build/label - unreleased
- [done] .xbt, .sbao, .xbg and .rtx viewers and converters - bundled ffmpeg for audio, bare .dds builds
- [done] .fc2model export and apply - actor, animation tag windows, shader property lists
- [done] Dark theme
- [done] jackall-cli with --json, and jackall-mi for the Vortex extension
- [in progress] Tutorial docs and screenshot scripts - run-all not re-run since the UI-script refactor; verify-in-game.ps1 never deployed
- [scrapped] Map editor on FC2Editor/FCE over host IPC - user: the wrong direction; the AVATAR port (18e58eab) is a quarry only
- [scrapped] Map tab per-sector view culling - culled almost nothing, refilled streams on turns (d8b0f884)
- [scrapped] Map tab Demo-mode speed-ups - user: a non-issue
- [scrapped] SoundBench as a JackAll tab - SoundBench stays standalone

## Approvals

- played: 49624dc 2026-10-29 - implied by the jackall-1.7.0 release
- page text: 49624dc 2026-10-29 - implied by the jackall-1.7.0 release; the BBCode predates 1.7.0 and still calls the Map tab read-only

## Checks

## Release order

## Collisions
