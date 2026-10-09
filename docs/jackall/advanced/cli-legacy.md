---
slug: /cli-legacy
sidebar_position: 3
title: Take features out of an old mod
description: List every change an old Far Cry 2 mod makes, sort the changes into features and build a mod with only the features you want, with jackall-cli's legacy commands
---

# Take features out of an old mod

Many old mods bundle a dozen changes in one `patch.dat`, and you may want only one of them. The
command line's `legacy` commands list every change a mod makes, sort the changes into features, and
build a mod layer with only the features you pick.

The example is Skip Intro again. It does three things, and you'll take only the first: a new game
skips the taxi ride. The zip is `skip-intro-mod.zip`, a repack that holds the mod's two scripts as
loose files.

:::caution[Not yet confirmed in game]
The picked mod hasn't been played.
:::

## List the changes

`legacy analyze` takes the mod (a `.zip`, `.7z`, `.rar` or a folder, with loose files or a whole
`patch.dat`) and a work folder to fill:

```
> jackall-cli legacy analyze --game "C:\Games\Far Cry 2" --from skip-intro-mod.zip --work work\skip-intro
Analyzed skip-intro-mod.zip
  sha256   : 6da307f20ef4ba535af53c9fc8ae13a52042d20e3dee3df6e707262eaf20fe53
  data        : 2 file(s)
  archive  : 2 file(s) and 0 fragment(s) differ; 0 match the base game
  changes  : 6 across 2 unit(s), 0 of them only takeable whole
Next: legacy changes --work work\skip-intro --group
```

A change is the smallest difference the file's format allows: a hunk of a script, one value of an
archetype, one entity of a sector. Each has an address, the file and the place inside it.
`legacy changes` lists them, or counts them by shape with `--group`:

```
> jackall-cli legacy changes --work work\skip-intro --group
      4  domino/user/a1sm01_townescape.a1sm01_mission.lua@L*  Text  e.g. - +-- Delete cutscene props RemoveEntity("2057061720366558035"); RemoveEntity("20570617203...  [-- {\v/} Domino auto-generated LUA script file]
      2  domino/user/master_world1.world1.lua@L*  Text  e.g. -Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "blackscreenfx"; +Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "xlackscreenfx";  [-- {\v/} Domino auto-generated LUA script file]
6 change(s) in 2 shape(s).
```

```
> jackall-cli legacy changes --work work\skip-intro
12261f372c Text   domino/user/a1sm01_townescape.a1sm01_mission.lua@L1784  - +-- Delete cutscene props RemoveEntity("2057061720366558035"); RemoveEntity("20570617203...  [-- {\v/} Domino auto-generated LUA script file]
53da98ecd1 Text   domino/user/a1sm01_townescape.a1sm01_mission.lua@L1785  -self[150].Seconds = 96; +self[150].Seconds = 0.1;  [-- {\v/} Domino auto-generated LUA script file]
a2f972c1a9 Text   domino/user/a1sm01_townescape.a1sm01_mission.lua@L1814  -self[166].Seconds = 8.15; +self[166].Seconds = 0.1;  [-- {\v/} Domino auto-generated LUA script file]
b2ff54faab Text   domino/user/a1sm01_townescape.a1sm01_mission.lua@L1816  -self[169].Seconds = 5; +self[169].Seconds = 0.1;  [-- {\v/} Domino auto-generated LUA script file]
616d34dc1f Text   domino/user/master_world1.world1.lua@L799  -Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "blackscreenfx"; +Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "xlackscreenfx";  [-- {\v/} Domino auto-generated LUA script file]
54cde8a0a7 Text   domino/user/master_world1.world1.lua@L801  -Boxes[PathID("Domino/System/PostFx.lua")].Out = self._type.f_0_Out; +Boxes[PathID("Domino/System/PostFx.lua")].Out = self._type.f_12_Out;  [-- {\v/} Domino auto-generated LUA script file]
6 change(s).
```

`--match` narrows the list to addresses that match a pattern, such as `"**/entitylibrary*.fcb/**"`.

:::warning[On a GOG install]
Analyzing a whole `patch.dat` built on the Steam version also lists every difference between the
Steam and GOG patches as changes. The Nexus release of Skip Intro gives 6,154 changes on GOG, where
the mod itself makes 11. See
[Import an old patch.dat mod](/jackall/legacy-import#keep-only-the-mods-changes).
:::

## Sort them into features

Which change belongs to which feature is written down by hand, one page per feature. The repo's
[`legacy` folder](https://github.com/jbebe/farcry-sdk/tree/main/legacy) has pages for Skip Intro,
Scubrah's Patch, Far Cry 2 Redux, Realism Plus and Far Cry 2 Editor With AI. Each page's front matter
claims its changes by address:

```yaml
---
title: Skip the opening sequence
kind: component
bundle: skip-intro
claims:
  - "Skip the opening sequence: a new game starts the town escape mission at the hotel, without the taxi ride"
status: located
systems: [missions]
match:
  - "domino/user/master_world1.world1.lua@L{799,801}"
---
```

`legacy check` makes sure the pages claim every change, each exactly once:

```
> jackall-cli legacy check --mod legacy\skip-intro --work work\skip-intro --features
Skip Intro: 4 page(s), 6 change(s), 0 unclaimed, 0 contested, 0 published claim(s) uncovered, 0 problem(s).
        2  hotel-wake-up-sooner (component, located)
        0  skip-intro (bundle)
        2  skip-jackal-briefing (component, located)
        2  skip-opening-sequence (component, located)
Clean: every change and every published claim is accounted for.
```

`legacy changes --unclaimed --mod legacy\skip-intro` lists what no page claims yet, while you write
them. [`legacy/README.md`](https://github.com/jbebe/farcry-sdk/blob/main/legacy/README.md) describes
the page format.

## Pick a feature

See what a feature takes, then build a layer with only that:

```
> jackall-cli legacy changes --work work\skip-intro --mod legacy\skip-intro --feature skip-opening-sequence
616d34dc1f Text   domino/user/master_world1.world1.lua@L799  -Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "blackscreenfx"; +Boxes[PathID("Domino/System/PostFx.lua")].PostFxName = "xlackscreenfx";  [-- {\v/} Domino auto-generated LUA script file]
54cde8a0a7 Text   domino/user/master_world1.world1.lua@L801  -Boxes[PathID("Domino/System/PostFx.lua")].Out = self._type.f_0_Out; +Boxes[PathID("Domino/System/PostFx.lua")].Out = self._type.f_12_Out;  [-- {\v/} Domino auto-generated LUA script file]
2 change(s).
```

```
> jackall-cli legacy pick --game "C:\Games\Far Cry 2" --mod legacy\skip-intro --work work\skip-intro --feature skip-opening-sequence --out picked\skip-opening
Picked skip-opening-sequence
  2 change(s): 1 unit(s) copied, 0 rebuilt from the base game, into picked\skip-opening
```

`--feature` can be repeated, and a bundle's name takes all of its features. The result is an
ordinary mod layer, here one file, `mods\domino\user\master_world1.world1.lua`:

```
> jackall-cli mod inspect picked\skip-opening --game "C:\Games\Far Cry 2"
Mod layer (1 file(s))
  root              : <top level>
  file overrides    : 1 (0 hash-addressed)
  .fcb fragments    : 0
  plugin files      : 0 (deployed to bin\plugins)
  ignored files     : 0 (readmes and the like)
```

Build it with `mod build --layer picked\skip-opening` (see
[Manage mods from the command line](/jackall/cli-mods)), or zip it and add it on the **Mods** tab
with **Import mod**.

To convert a whole old mod instead, `mod import-legacy --from <zip> --out <folder>` does what
**Import legacy mod** does, into a layer folder.
