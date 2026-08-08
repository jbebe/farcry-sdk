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

- [ ] Review the Domino viewer because it needs a lot of improvements
  - Review code
  - Revamp the visual interface, find a good graph wpf package
- [x] A diffable text form for `.mgb` packages — shipped as JackAll's XML interchange format
  (`jackall mgb decode` / `mgb encode`, plus Export/Import XML in the editor tab). Deliberately its
  own schema rather than `.mgm`, borrowing only `.mgm`'s recovered vocabulary for element and
  attribute names. `.mgm` was ruled out because it *cannot* round-trip a shipped `.mgb`: it has no
  construct for the per-file type table, the pool-count block, header bytes 5–7/13, or embedded font
  blobs; it parses floats through `atof`; and it authors names as strings the loader CRC32s, which
  does not invert — so exporting an existing package would mean inventing names and breaking every
  cross-reference. See [`.mgb`](/docs/file-formats/mgb#the-xml-interchange-format).
- [ ] `.mgm` emission for `.mgb` packages — the *other* half of the original idea, still open. Since
  dispatch is purely by extension and `.mgb.desc` is already plain editable XML naming its own
  `.mgb`, a mod could in principle ship a `.mgm` the retail engine parses directly, with no binary
  writer involved: `CFileNameNomad::GetFileType` returns `1` for `.mgm` and `2` for `.mgb`, and
  `Engine::LoadPackage` picks `Factory::MakeLoadVisitor` (the `CMarkupSTL` XML parser) for type `1`.
  Not dead code in the retail client — `Dunia.dll` has `magma::LoadVisitor::vftable` @ `0x10eea404`
  and `magma::PackageMarkupSTL::vftable` linked in, with a complete 1:1 `ReadX` per class mirroring
  `BinaryLoadVisitor`. Now cheap to attempt: it would be a third `IMgbCodec` implementation over the
  same `Serialize` descriptions. Risks to settle first: linked ≠ exercised (Ubisoft may never have
  run this path in a retail build); no sample `.mgm` exists anywhere in the game data, so the
  document shape — attribute-vs-element text, and how a class is named — has to be reconstructed from
  `ReadPackage` (`0x0a0688e0`) and `ReadArea`'s child loop rather than checked against a real file;
  and the XML reader treats every element as optional, so an incomplete `.mgm` degrades silently
  instead of erroring. Only viable for **newly authored** packages, never for round-tripping a
  shipped one — the hash problem above is unavoidable. Field names are already recovered (see
  [`.mgb` field names](/docs/file-formats/mgb-field-names)).

## Tools/FCSE

- [x] Give FCSE its own settings page instead of borrowing the stock Game tab. Delivered
  2026-08-08 — FCSE authors its own Magma package, feeds it to the engine through a hooked file
  reader, and binds a private page to it by name. Full trail in `tools/FCSE/PLAN-own-page.md`; the
  status block at the top lists the six findings that cost the most time, several of
  which generalise well beyond this feature (vtable slot indices not porting between the ELF and
  `Dunia.dll`; magma resolving resources by path *hash* so no loose file can be reached by name; a
  package's identity doubling as its texture root).
- [ ] Native YES/NO controls on that page via `CSettingsPage::AddBoolSetting`. The rows build
  correctly — the slot resolves, the widget binds, both entries are added — but clicking one crashes
  inside the engine's own handling of the `CValueListSetting`, with no FCSE code in the path. Parked
  behind `Own page native toggles` in `fcse.ini`; the unexplored thread is that object's event slot
  2 (`0x1081c260`), which keys on magma hash `0x61904E45` and dereferences `setting+0x48`.
- [ ] Slider and choice settings (`FCSE_SettingType_Slider` / `_Choice` in `include/plugin_api.h`).
  Needs `AddSliderSetting`/`AddValueListSetting` and a second slot bank in `fcse.mgb` pointing at
  `common.mgb` area `62EA6603` rather than `652FD37C`.

## Tools/"dll plugins"

- [x] There are things you can't manipulate via game assets. The Dunia engine is versatile but they had to force some kind of domain on top of it so that they don't create the next Unity or Unreal. That abstract domain logic/configuration is what stayed in Dunia.dll. So if any mod wants to alter these values, they need to ship their own patched version of Dunia.dll. This works only for one mod, if another mod wants to do the same, they can't. That's why we need a plugin system similar to [SKSE](https://www.nexusmods.com/skyrimspecialedition/mods/30379). We need a new launcher that the "Far Cry SKSE" ships, plus a folder for mods sitting in dlls. This launcher would be built with [`/LARGEADDRESSAWARE`](https://learn.microsoft.com/en-us/cpp/build/reference/largeaddressaware-handle-large-addresses?view=msvc-170) to enable 4GB address space to be safe when lots of mods are present in the game.
  - Built as `tools/FCSE` (FCSE.exe) — see [FCSE](/fcse) for the flagship summary and `tools/FCSE/README.md` for the full design. Built and smoke-tested outside the game process (missing-Dunia.dll handling, plugin discovery, all three conflict-rejection paths).

## Tools/vortex-farcry2

- [ ] Test whether the two performance improvements helped or not at all.
- [ ] Validate against a real Vortex install: install→enable→deploy→purge with a mod from each of the three buckets (legacy patch, FCSE plugin, asset mod), the vanilla-baseline confirmation dialog, and a genuine load-order conflict. `npm test` only proves the bundle loads against a stubbed API, not that any of this actually works.
- [ ] Resolve `gameart.jpg`: the extension's README calls it a placeholder ("replace with real key art before publishing"), but the committed file is official Far Cry 2 promotional art (Ubisoft-owned) at 1438x810, not the stated 640x360 tile. Decide whether it's cleared to redistribute, or source a clean replacement.

## Mod

- [ ] Create our first mod because that was the original plan
