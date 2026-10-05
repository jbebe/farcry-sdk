---
title: Buddy quests complete on their objective
kind: component
bundle: gameplay
claims:
  - "Buddy sidequest missions will now be completed upon completing the main objective(s) (no need to debrief with your buddy)"
status: located
systems: [missions, buddies]
match:
  - "domino/user/common_customboxes.missioncompletebroadcast.lua@L{81,241,242,267,269,434,436}"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmission_and_pawnbrief_selection.lua@L{304,307,316,319,426,1297,1525}"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w1.lua@L669"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w2.lua@L603"
  - "domino/system/setsidequestmissionstate.lua@L{34,45}"
  - "worlds/*/generated/world*.mapsdata.fcb/_layout.xml#delete[{A1SM03_MikesDefenseMarker_*,Objectives.ObjectivePoint_BSQ*}]"
exclude: []
requires: [concurrent-missions, missions-mission-completed]
verified: diff
---

# Buddy quests complete on their objective

A buddy side quest (BSQ, offered at Mike's bar) now completes as soon as its main objective is done.
The trip back to the bar to debrief with the buddy is gone, along with its map marker, and the bar
offers the next quest right away.

## How

- `mikesplace_bsqmission_and_pawnbrief_selection.lua`: where the bar's graph checks a quest whose main
  objective is complete, the comparison now uses `AcceptedBuddyMissionID` (`@L304`, `@L316`,
  `@L426`, was `AcceptedMissionID`) and a match goes straight to the debriefing's `Succeeded`
  handlers `f_108_Succeeded` / `f_87_Succeeded` (`@L307`, `@L319`) instead of selecting the debrief
  pawn briefing. Those handlers now call a new `BSQMission_Complete` (`@L1297`, `@L1525`, was
  `SideMission_Complete`).
- `common_customboxes.missioncompletebroadcast.lua`: `@L81` adds `BSQMission_Complete`, which runs
  the vanilla completion order but compares and completes `AcceptedBuddyMissionID` (`@L267`, `@L269`:
  the vanilla `f_17_Out_0` becomes `f_17_Out_3`; `@L434`, `@L436`); `@L241`, `@L242` add
  `f_15_Out_BSQ`, which resets `AcceptedBuddyMissionID` and `BSQBuddyName` to `"x"` afterwards.
- `mikesplace_bsqmanager_w1.lua@L669` / `_w2.lua@L603`: after new pawn briefings are selected, the
  graph fires Mike's "player left the area" handler itself, so new quests are generated without the
  player leaving.
- `domino/system/setsidequestmissionstate.lua@L34`, `@L45`: the box's `Accepted`/`Succeeded` calls
  to `GetBuddiesManager():SetSidequestMissionState` are commented out; per the file's new header the
  logic moved to `MissionCompleted`, which calls both when a BSQ completes.
- The debrief markers are deleted from `mapsdata`: 13 entities in `world1.mapsdata.fcb` named
  `A1SM03_MikesDefenseMarker_1`..`_14` (vanilla's name, but each is a quest's step-2 marker, `StateId`
  `BSQ01_02`..`BSQ15_02`, `ObjectiveId` `BSQ??_02_NBM_01`) and 12 `Objectives.ObjectivePoint_BSQ*`
  in `world2.mapsdata.fcb` (`BSQ14_02`..`BSQ26_02`). `A1SM03_MikesDefenseMarker_10` (`BSQ11_04`) is
  kept and moved into the `bsq11` layer by `concurrent-missions`.

## Depends on

- `concurrent-missions`: `AcceptedBuddyMissionID` is set by its mission-accepted broadcast, and much
  of the bar's graph changes live there.
- `missions-mission-completed` claims `domino/system/missioncompleted.lua@L32`, which on a `BSQ` completion
  shows the `MISSION_CONCLUDED` message, pays the reward (`buddy-missions-20-diamonds`), counts
  `BSQMissionsCompleted` (`golden-ak47-shop`) and calls `SetSidequestMissionState` `Accepted` then
  `Succeeded` so the buddy's history updates.

## Uncertain

- `objectivestate.lua` (on `concurrent-missions`) still enables the deleted step-2 markers; those calls
  find nothing.
- Why BSQ11's last marker is kept is not stated; it may be a real objective rather than a debrief.
