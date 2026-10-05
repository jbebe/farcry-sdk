---
title: Convoy completes when someone else destroys the truck
kind: component
bundle: fixes
claims:
  - "Fixed an issue where if a convoy truck wasn't destroyed by the player the mission wouldn't complete correctly, preventing future convoy missions from starting"
status: located
systems: [missions, patrols, save]
match:
  - "domino/user/sidemissions/convoymissions.convoy_missions.lua@L79"
  - "**/entitylibrary*.fcb/ghostpatrols/convoy/convoytarget.xml#Entity/Components/CPersistComponent/selLevel"
  - "worlds/*/generated/world*.mapsdata.fcb/convoymission_*#Components/CPersistComponent/selLevel"
exclude: []
requires: []
verified: diff
---

# Convoy completes when someone else destroys the truck

A convoy mission whose truck is destroyed by enemy AI (or anything but the player) now counts as
completed, so the next convoy mission is still offered.

## How

- `domino/user/sidemissions/convoymissions.convoy_missions.lua@L79`: box 65 is the
  `MissionCompleteBroadcast` the failure branch fires (`ConvoyMission` box 33's `Failure` ->
  `f_33_Failure`). Its `BroadcastSent` is rewired from `f_65_BroadcastSent` (disable the mission
  layer, re-enable the disable layer, fail) to `f_85_BroadcastSent`, the success branch's
  continuation (adds 1 to `MissionCounterConvoy` and carries on to the next convoy). Comment: "FIX:
  If a enemy AI destroys the convoy truck, the mission is still completed".
- `CPersistComponent/selLevel` `1` (`Limited`) -> `3` (`Critical`) on the convoy truck archetype
  `GhostPatrols.Convoy.ConvoyTarget` (`worlds/world1` and `worlds/world2` `entitylibrary.fcb`) and on
  the `CConvoyMission` controller entities `ConvoyMission_*` in `world1.mapsdata.fcb` (7) and
  `world2.mapsdata.fcb` (4).

## Uncertain

- With `convoy-distance-fail` applied, `ConvoyMission.lua` never reports `Failure` any more, so the
  `@L79` rewiring is only reached without that page; it reads as the earlier fix for the same symptom.
- That the `selLevel` raise belongs to this fix is an inference: a fully persisted truck and convoy
  controller would keep a convoy's state across a save and reload; nothing in the mod says so.
