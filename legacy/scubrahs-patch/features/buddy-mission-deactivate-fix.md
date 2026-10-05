---
title: BSQ08 deactivates when done
kind: component
bundle: fixes
claims:
  - "Fixed a scripting error that caused a buddy mission to never deactivate after completion"
status: located
systems: [missions, buddies]
match:
  - "domino/user/bsq08_theprototype.bsq08_mission.lua@L187"
exclude: []
requires: []
verified: diff
---

# BSQ08 deactivates when done

The buddy side quest "The Prototype" (BSQ08) switches itself off once finished, instead of staying
active for good.

## How

`domino/user/bsq08_theprototype.bsq08_mission.lua@L187` inserts, commented "FIX: Mission doesn't
deactivate on picking up the suitcase or debriefing with buddy at Mike's", a call to
`GetMission("Missions/BuddySideQuests/BSQ08"):Disable()` at the start of `f_12_Out`, the handler that
runs when the suitcase is picked up (it shows "You have the suitcase. Go see Marty at Mike's Place").

## Depends on

- With `buddy-quests-complete-on-objective` the quest also completes at that point; this line only
  switches the quest's layer off.
