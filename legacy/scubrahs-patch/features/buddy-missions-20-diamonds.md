---
title: Buddy side quests pay 20 diamonds
kind: component
bundle: balancing
claims:
  - "All buddy side missions now reward you with 20 diamonds on completion"
status: located
systems: [economy, buddies]
match: []
exclude: []
requires: [missions-mission-completed]
verified: diff
---

# Buddy side quests pay 20 diamonds

Completing any buddy side quest now pays 20 diamonds; in vanilla these quests pay none.

## How

The reward is a few lines inside the one large hunk `domino/system/missioncompleted.lua@L32`, which
this page does not claim because it is a single change shared by several features (it is claimed by
`missions-mission-completed`). In `MissionCompleted`'s `In`, when the completed mission id contains `BSQ`, the
hunk pushes a `MikesPlace`/`MISSION_CONCLUDED` HUD message, plays sound `0x004f014b`, calls
`AddDiamonds(20)` and adds 20 to the script-side `DiamondCounter` (`economy-diamond-counter`). The
same branch counts `BSQMissionsCompleted` for the golden AK-47 unlock and fixes the buddy history
update.

## Depends on

- `domino/system/missioncompleted.lua@L32` - the hunk that holds the reward, claimed by
  `missions-mission-completed`. Picking this page pulls that whole hunk: it also removes buddy-rescue
  objective icons, re-enables the airport enemies, fixes the cinematic HUD mode after `A1SM01`/`A2SM08`,
  shows contextual "mission concluded" messages for convoy and assassination missions and unlocks the
  golden AK after 10 buddy quests.
- The quest has to reach `MissionCompleted` without the debrief, which is the job of
  `buddy-quests-complete-on-objective`.
