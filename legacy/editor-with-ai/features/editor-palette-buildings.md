---
title: Hidden building pieces in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[2]/**"
exclude: []
requires: []
verified: diff
---

# Hidden building pieces in the editor palette

The palette's Buildings folder grows from 239 to 412 entries. Each style folder gains walls, floors,
roofs and whole buildings that the template world's library has but the vanilla palette does not
list.

## How

`ingameeditor/object_inventory.xml`, top-level `Directory_Buildings` (`Directory[2]`): 173 entries
are added to the eight existing sub-folders. Nothing is changed or removed. Per folder: Dogon 43 to
61, Fishing 18 to 37, Fort 33 to 55, Huts 23 to 80, Industrial 30 to 54, Urban 37 to 47, Shanty 22
to 45 (Colonial keeps its 33).

Of the added archetypes, 155 are `IGE_Archetypes.AutoGen.*`. Vanilla ships these commented out in
its "DO NOT USE: AutoGen objects" block (see
[editor palettes](../../../docs/docs/file-formats/object-inventory.md)). The rest are six
`IGE_Archetypes.Doggon.*`, four `OA_Buildings.Buildings.*`, three `OA_CoverObjects.CoverObjects.*`,
two `OA_Burnable.Burnable.*`, one `OA_MissionObjectives.MissionObjectives.*` and
`props.World2_PrisonCell`. Most `Display` labels are the raw archetype or mesh names. All resolve in
the template world's vanilla library.

## Uncertain

- Vanilla commented these out on purpose ("DO NOT USE"). Which pieces misbehave (no collision, no
  LOD, wrong pivot) has not been checked piece by piece.
