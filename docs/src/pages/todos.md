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

## Tools/JackAll

- [x] Pipe the converters to the CLI tool to please the die-hard modders
- [ ] Extend filtering
  - Add parent folder name to modules (e.g. dlc1/entitylibrary) otherwise it's confusing
  - Add module search
  - Add checkbox to filter to include links too (they are really annoying if you're not searching for them)

## Tools/"dll plugins"

- [ ] There are things you can't manipulate via game assets. The Dunia engine is versatile but they had to force some kind of domain on top of it so that they don't create the next Unity or Unreal. That abstract domain logic/configuration is what stayed in Dunia.dll. So if any mod wants to alter these values, they need to ship their own patched version of Dunia.dll. This works only for one mod, if another mod wants to do the same, they can't. That's why we need a plugin system similar to [SKSE](https://www.nexusmods.com/skyrimspecialedition/mods/30379). We need a new launcher that the "Far Cry SKSE" ships, plus a folder for mods sitting in dlls. This launcher would be built with [`/LARGEADDRESSAWARE`](https://learn.microsoft.com/en-us/cpp/build/reference/largeaddressaware-handle-large-addresses?view=msvc-170) to enable 4GB address space to be safe when lots of mods are present in the game. 

## Mod

- [ ] Create our first mod because that was the original plan
