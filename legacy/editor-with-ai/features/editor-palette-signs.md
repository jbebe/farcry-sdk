---
title: Street and location signs in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[0]/**"
exclude: []
requires: []
verified: diff
---

# Street and location signs in the editor palette

The palette's Multiplayer > Signs folder gains the campaign's road signs and named-place signs:
"Stop", "One way", "Limit 50", the safehouse sign, and the boards for places like Mike's Bar,
the Petro Sahel and the Ranger Station.

## How

`ingameeditor/object_inventory.xml`, top-level `Directory_Multiplayer`: two new `Directory_Faction`
("Signs") sub-folders are added beside the vanilla one (`Directory[0]/Directory[+1]` and `[+2]`, two
changes). They hold 67 entries, 59 of them signs the vanilla palette lacks: 14
`OA_StreetSigns.StreetSigns.*` (country and urban street signs, several in their `.Multi` form, and
`SafeHouseSign`), 44 `OA_StreetSigns.MissionObjectiveSigns.*` (named-place signs from `AfriQaTelecomSign`
to `WeelegolVillage`) and `OA_StreetSigns.MissionObjectiveSignsSafeHouse.SafeHouse_A1BU00`. Each
`Display` is the archetype name.

Every archetype named is in the template world's vanilla entity library. The folder grows from 64
to 131 entries. Nothing vanilla is removed.

## Uncertain

- Whether the single-player-only signs (no `.Multi` variant) replicate to clients in a multiplayer
  match has not been checked.
