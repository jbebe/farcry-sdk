---
title: Rocks, plants and desert prefabs in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[4]/**"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{Razor48,Razor50,Razor55,razor73,razor75,razor76,razor78,razor81,razor85,razor87,razor94}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{Razor48,Razor50,Razor55,razor73,razor75,razor76,razor78,razor81,razor85,razor87,razor94}]{,@value}"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# Rocks, plants and desert prefabs in the editor palette

The palette's Natural folder grows from 185 to 395 entries: about 150 more rocks, extra plants and
trees, ready-made desert vignettes, and eleven custom jungle and desert plants.

## How

`ingameeditor/object_inventory.xml`, top-level `Directory_Natural` (`Directory[4]`), 171 changes:

- **Rocks** goes from 105 to 253 entries, split into four "Rocks" sub-folders. Most of the new ones
  are `IGE_Archetypes.AutoGen.*` from vanilla's commented-out block (172 AutoGen entries across the
  folder).
- **Plants and Bush** 22 to 31, **Trees** 30 to 36, **Other** 28 to 75.
- 18 prefabs from the template world's `tmpla.managers.fcb`: the `desert_terrain_*` groups (aloes,
  bones, bushes, dry trees, rock groups, rocks with bones or bushes, a tree group) and
  `Trash_TrashPile01`.
- 11 entries from the custom library ([`editor-razor-library`](editor-razor-library.md)):
  `Custom.RaZoR_Object.Natural.Cactus2`, `Liana1`, `AcaciaTree4`, `JungleRoots1`, `Bachia1`, `2`,
  `4`, `FicusTree3`, `7`, `StripeLeaves1` and `GroundDetail1`. Their `Razor48`/`50`/`55` and
  `razor73`..`94` strings label them ("Cactus 2", "Liana 1", "Acacia 4", "Jungleroots",
  "Bachia 01", "Ficus Tree 03", "Leave Stripe", "Jungle Plants" and so on), and are this page's.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) for the eleven custom plants.

## Uncertain

- The custom library declares 27 plants. The palette lists 11 of them, and strings exist for more,
  for example "Bachia 03" and "Ficus Tree 01". Whether the rest were left out on purpose is not
  known.
