---
title: Editor labels in seven more languages, older and in English
kind: component
bundle: strings
status: located
systems: [ui]
match:
  - "languages/{czech,french,german,hungarian,italian,polish,spanish}/oasisstrings.fragment.xml#InGameEditor*/**"
exclude: []
requires: []
verified: diff
---

# Editor labels in seven more languages, older and in English

In a Czech, French, German, Hungarian, Italian, Polish or Spanish game, the map editor's own
interface turns English, and the objects and textures the editor mod adds get English names. The
set is an older one than the English game gets: about a hundred of the newer entries have no label
in these languages.

## How

The seven languages' `oasisstrings.rml` are one English table, shipped whole (see
`strings-english-in-other-languages`). This page is its seven `InGameEditor*` sections
(`InGameEditor`, `InGameEditor_PC`, `InGameEditor_Objects`, `InGameEditor_Textures`,
`InGameEditor_Collections`, `InGameEditor_Splines`, `InGameEditor_Wilderness`), 13,845 changes
over the seven languages, 1,914 (French) to 2,018 (Spanish) each:

- **276 new labels, the same in every language.** 198 objects and 78 textures of the editor mod:
  static, physics and fake weapons (`Static_Weapon*`, `Physic_Weapon*`, `FakeWeapon*`), Fortunes
  Pack objects and vehicles (`DLC_object*`, `dlc1_vehicle*`), the first twelve particle objects
  (`Razor1`-`Razor12`: flies, waterfalls, smoke), custom concrete, wood, brick, dirt, ceramic and
  cement textures (`Custom_*`), the `Janne252_*` folders, and two stray keys, `testi` and `vanhat`
  (Finnish for "test" and "old").
- **The rest replace the translation with English**: 1,652 in German, 1,740 in Czech. Among them
  `InGameEditor_PC/EDITOR_NAME`, the editor's title, becomes "Far Cry® 2 Map Editor - Running with
  Janne252's Mod. Xfire contact: Janne252".

Compared with the English table the mod ships, these sections lack 112 labels (104 objects,
8 textures): `Razor13`-`Razor94` (more particles, wrecks, buildings, trees, Datsuns, swamp boats,
buggies, quad bikes), `Static_Weapon54`-`56`, `Physic_Weapon59`-`60`, the `Directory_*` folders and
others. Four labels differ: `Razor11` "Dark Smoke (S)" (English "Dark Smoke (S) 01"), `Razor12`
"Dark Smoke (M)" ("Light Smoke (M)"), `Janne252_DLC_folder_main` "DLC" ("Fortunes Pack") and
`Janne252_DLC2_folder` "Community DLC Objects" ("DLC2 required Ingame!"). These are an earlier
state of the same editor mod's labels, which is why the copies are taken to come from Janne252's
release.

These labels name entries of the editor's object and texture inventories (`ingameeditor/**`), and
mean nothing without them. The English labels are on the editor pages of this mod.

## Uncertain

- What the editor shows for the 112 entries with no label here (the key, or an empty line) is not
  checked.
- Whether a German player would rather keep the German editor and get only the 276 new labels is a
  choice the mod did not offer; a pick of this page takes the English interface too.
