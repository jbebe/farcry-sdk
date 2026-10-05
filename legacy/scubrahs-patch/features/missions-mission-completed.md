---
title: MissionCompleted box rewrite
kind: shared
systems: [missions, buddies, economy, ui]
match:
  - "domino/system/missioncompleted.lua@*"
exclude: []
requires: []
verified: diff
---

# MissionCompleted box rewrite

`domino/system/missioncompleted.lua`, the Domino box every mission graph runs on completion, gains one
inserted block in `In()` (`@L32`) that several features live in, and loses its last line break
(`@L39`). A text hunk cannot be split, so the block is a page of its own and each feature that needs
it requires this page:

| Part of the block | Feature |
|---|---|
| on `A1LM05`, `Enable()` on `Missions/_DisableForMission/W1D3_A15Airstrip_EnemiesSTP` | `airport-respawn-fix` |
| `SetCinematicUIMode(1)` after `A1SM01` and `A2SM08` | `save-after-missions-fix` |
| after a buddy side quest (`BSQ`): 20 diamonds, buddy history update | `buddy-missions-20-diamonds`, `buddy-quests-complete-on-objective` |
| after a buddy side quest: golden AK-47 unlock count | `golden-ak47-shop` |
| map-icon clean-up and `PrimaryMissionActive` reset after `A1BU01`-`A1BU04`, `A2BU06`, `A2BU07`; contextual "Mission Concluded" popups for convoy (`CV0`) and assassination (`AS0`) missions | `concurrent-missions` |

Taken alone, the block switches all of these on together; picking one feature brings the rest of the
block with it.
