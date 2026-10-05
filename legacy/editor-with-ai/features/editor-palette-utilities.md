---
title: Hundreds of props, doors, fences and furniture in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[3]/**"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{Razor34,Razor35,Razor36,Razor39}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{Razor34,Razor35,Razor36,Razor39}]{,@value}"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# Hundreds of props, doors, fences and furniture in the editor palette

The palette's Utilities folder grows from 416 to 1,048 entries. It gains campaign props and detail
objects, opening doors, the weapon-shop crates, the player's gadgets as props, ladders, and fences
sorted by building style.

## How

`ingameeditor/object_inventory.xml`, top-level `Directory_Utilities` (`Directory[3]`), 271 changes.
Per sub-folder:

| Folder | Vanilla | Mod | Added sub-folders |
|---|---|---|---|
| Bridges | 17 | 18 | |
| Crates and Containers | 26 | 79 | 1 |
| Detail Objects | 95 | 449 | 4 (PhysX, "Bottle & Other PhysX", "No PhysX", decor) |
| Doors and Windows | 9 | 79 | 3 |
| Fences | 103 | 158 | 8, one per building style |
| Furniture | 50 | 93 | 1 |
| Quays | 4 | 19 | |
| Stairs and Ladders | 20 | 39 | 1 |
| Structures | 42 | 53 | |
| Other Cover | 50 | 61 | |

The new archetypes come mostly from the template world's own library: 266
`IGE_Archetypes.AutoGen.*`, 156 `OA_DetailObjects.DetailObjects.*`, 57 `props.Props.*`, 21
`object_archetypes.Infrastructures.*`, 16 `OA_BuildingAccessories.*`, 11 `OA_Furniture.*`, 10
`OA_MissionObjectives.*`, 8 `OA_CoverObjects.*` and a few campaign props (`props.World1_HotelRoom.*`,
`props.World2_RadioDJ.*` and others). Notable ones:

- 12 `Interactive.InteractiveDoors.*`, the campaign's animated and locked doors (Dogon, Industrial,
  Colonial, Barge, Urban, Mike's Place, Prison).
- `Interactive.MagicCrates.PrimaryCrate`, `SecondaryCrate` and `SpecialCrate`, the weapon-shop
  crates.
- `gadgets.Equipped.Map`, `CompassSingle`, `CompassMulti`, `Compass_Vehicle`, `Watch` and
  `Monocular`.
- Wooden ladders (`IGE_Archetypes.AutoGen.LadderWood*steps`, `OA_Ladder.Ladders.*.Multi`).
- 14 prefabs from the template world's `tmpla.managers.fcb`: eleven `Board_*_Version*` billboards,
  `Equipment_Cages01`/`02` and `Industrial_Boxes01`.
- 4 entries from the custom library ([`editor-razor-library`](editor-razor-library.md)):
  `Custom.RaZoR_Object.TownFence1`..`3` and `Barrel_NoPhysSync`, labelled "Town Fence 01".."03"
  and "No online sync physic test" by their `Razor34`..`36`/`Razor39` strings, which are this
  page's.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) for the four custom entries.

## Uncertain

- One added entry, `OA_Furniture.Furniture.buddytable_flip_bk.Multi`, names an archetype the
  template library does not have, so it spawns nothing.
- Whether the interactive doors and magic crates work in a map (the crates need the weapon bazaar,
  which [`ai-editor-mode-services`](ai-editor-mode-services.md) adds to the editor mode) has not
  been checked.
