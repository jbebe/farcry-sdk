---
title: Todos
description: Open tasks across the repository, docs, JackAll, and mod work.
---

# Todos

A running task list across the whole project.

## Repository

*nothing*

## Docs

- [ ] Improve JackAll docs

## Reverse

- [ ] Reverse Far Cry 2 Xbox360 prototypes with XEX decompiler
- [ ] Reverse other similar Ubisoft titles:
  - https://hiddenpalace.org/Far_Cry_4_(Oct_25,_2014_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Splinter_Cell:_Chaos_Theory_(Jan_18,_2005_Multiplayer_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Rainbow_Six:_Lockdown_(Jan_16,_2006_demo)
  - https://hiddenpalace.org/Assassin%27s_Creed_(Feb_15,_2008_prototype)

## Tools/JackAll

- [x] Pipe the converters to the CLI tool to please the die-hard modders
- [x] Extend filtering
  - let me search for a file by hash too
  - Add parent folder name to modules (e.g. dlc1/entitylibrary) otherwise it's confusing
  - Add module search
  - Add checkbox to filter to include links too (they are really annoying if you're not searching for them)
- [ ] Review the Domino viewer because it needs a lot of improvements
  - Review code
  - Revamp the visual interface, find a good graph wpf package

## Tools/"dll plugins"

- [x] There are things you can't manipulate via game assets. The Dunia engine is versatile but they had to force some kind of domain on top of it so that they don't create the next Unity or Unreal. That abstract domain logic/configuration is what stayed in Dunia.dll. So if any mod wants to alter these values, they need to ship their own patched version of Dunia.dll. This works only for one mod, if another mod wants to do the same, they can't. That's why we need a plugin system similar to [SKSE](https://www.nexusmods.com/skyrimspecialedition/mods/30379). We need a new launcher that the "Far Cry SKSE" ships, plus a folder for mods sitting in dlls. This launcher would be built with [`/LARGEADDRESSAWARE`](https://learn.microsoft.com/en-us/cpp/build/reference/largeaddressaware-handle-large-addresses?view=msvc-170) to enable 4GB address space to be safe when lots of mods are present in the game.
  - Built as `tools/FCSE` (FCSE.exe) — see [FCSE](/fcse) for the flagship summary and `tools/FCSE/README.md` for the full design. Built and smoke-tested outside the game process (missing-Dunia.dll handling, plugin discovery, all three conflict-rejection paths).

## Tools/vortex-farcry2

- [ ] Validate against a real Vortex install: install→enable→deploy→purge with a mod from each of the three buckets (legacy patch, FCSE plugin, asset mod), the vanilla-baseline confirmation dialog, and a genuine load-order conflict. `npm test` only proves the bundle loads against a stubbed API, not that any of this actually works.
- [ ] Resolve `gameart.jpg`: the extension's README calls it a placeholder ("replace with real key art before publishing"), but the committed file is official Far Cry 2 promotional art (Ubisoft-owned) at 1438x810, not the stated 640x360 tile. Decide whether it's cleared to redistribute, or source a clean replacement.

## Mod

- [ ] Create our first mod because that was the original plan
