# Vortex extension

Purpose: Far Cry 2 support in Vortex - install, order, deploy and purge mods, handing every archive build to JackAll's jackall-mi.
Stage: maintenance

## Features

- [done] Game discovery for Steam and GOG, and the modded-baseline check before the first build
- [done] Legacy patch.dat/patch.fat mods converted at install by mod import-legacy
- [done] FCSE loader installed to bin\
- [done] Mod layers with mods\ and plugins\ at the archive root - plugins mirrored into bin\plugins
- [done] Deploy builds patch.dat through mod build; purge restores the vanilla archives
- [done] Load order page - the bottom mod wins
- [done] Conflicts notification listing every change a mod lost
- [done] Fragment merging of every container JackAll splits - controls, .mgb, brains, fonts, depload, MOVE, strings
- [done] FCSE fetched from Nexus when a plugin mod needs it, warning icon in the FCSE column
- [done] FCSE made Play's default launcher - open bug: FCSE_TOOL.queryPath reads a parameter Vortex never passes
- [done] Asks before starting FarCry2.exe with plugin mods - cancelling shows "Failed to run tool" in Vortex 2.8
- [done] Game start waits for a running build
- [done] Bundled jackall-mi, or a local CLI build through JACKALL_MI
- [scrapped] Showing the discovered FCSE tool or primary tool on Vortex's Tools page - the user dropped it

## Approvals

- played: 49624dc 2026-10-29 - implied by the vortex-0.6.0 release
- page text: 49624dc 2026-10-29 - implied by the vortex-0.6.0 release; the BBCode still describes 0.1.0 (Data_Win32 asset mods)

## Checks

## Release order

## Collisions
