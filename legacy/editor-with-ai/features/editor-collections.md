---
title: Every vegetation collection in the editor's brush palette
kind: component
bundle: editor-content
status: located
systems: [world, environment, ui]
match:
  - "ingameeditor/collection_inventory.xml@**"
exclude: []
requires: []
verified: diff
---

# Every vegetation collection in the editor's brush palette

The editor's vegetation brushes (collections of trees, bushes, grass and rocks painted over an area)
go from 29 to 99. The mod lists nearly every collection the template world defines, among them the
campaign's oases, dense jungle, jungle heart and desert sets.

## How

`ingameeditor/collection_inventory.xml`, compared as text (13 hunks). The file is rewritten as
"Collection Inventory of a the maximum", credited in a header comment to "Far Cry 2 - Multi...
Editor v1.3.2.2":

- 13 entries vanilla ships commented out are enabled: `FCX_Oasis01`, `FCX_SavannahToDesert03`,
  `FCX_Savannah01`, `FCX_BushSavannah01`, `FCX_DenseCoverSavannah01`/`02`,
  `FCX_RocksSavannah02`/`03`, `FCX_WoodlandSavannah02`, `FCX_JungleDefoliant01`,
  `FCX_DetailsJungle01`, `FCX_GrassJungleC` and `FCX_RiverbankGrass01`. The two woodland entries
  move to a new Woodland folder.
- New folders "Desert", "Savannah", "Woodland" and "Jungle" (each but Woodland with a "Grass and
  bushes" sub-folder) and "Others" add 56 more: `FCX_Oasis02`/`03`, `FCX_Desert01`/`02`,
  `FCX_GrassDesert01`, `FCX_DenseJungle00`..`02`, `FCX_JungleHeart01`/`03`, `FCX_JungleTrees01`,
  `FCX_HighGrassSavannah01`, `FCX_HarvestSavannah01`, `FCX_DeadTrees01`,
  `FCX_FakeMountainJungle01`, `Tire` and others. Most have a generic `Display` ("Desert",
  "Savannah", "Jungle") and no `ZoneLogic` or `SectorCost`. `FCX_BushSavannah02` is listed twice,
  once pointing at `FCX_BushSavannah_MP`.
- A closing comment lists collections to avoid: some duplicates and "does not work" ones from a
  2007 demo (`DoNotUse_*`, `OldGrasscompare`, `FCX_Artemisia*`, `FCX_Greenhouse01`).

All 98 collection names the file uses are among the 144 collections in the template world's vanilla
`tmpla.managers.fcb`. No collection is added. (The mod's only change to that manager is on
[`noise-editor-collection-twins`](noise-editor-collection-twins.md).)

## Uncertain

- **The file is not well-formed XML.** The last folder, "Others", is never closed:
  `</CollectionInventory>` follows its two entries. The spline palette
  ([`editor-road-splines`](editor-road-splines.md)) has the same slip. Both come from a released
  editor mod, so the editor's parser probably tolerates it. Whether it does, stops there, or rejects
  the whole palette has not been checked in game.
- An entry with no `SectorCost` may cost nothing against the sector budget. An entry with no
  `ZoneLogic` gives the painted area no AI zone logic. Neither effect is checked.
