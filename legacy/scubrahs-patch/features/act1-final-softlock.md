---
title: No soft-lock after the last Act 1 mission is assigned
kind: component
bundle: fixes
claims:
  - "Fixed an issue where the game could become soft-locked after assigning the final mission of Act 1"
status: unresolved
systems: [missions]
match: []
exclude: []
requires: []
verified: diff
---

# No soft-lock after the last Act 1 mission is assigned

Taking the final Act 1 story mission no longer leaves the game unable to progress.

## How

No change found. None of the mod's twenty `Dunia.dll` patches is about mission assignment except the
two on `buddy-rescue-optional`; if the soft-lock was a buddy rescue forced on the player after the
last Act 1 mission, that page's patch is the fix (inference).

Searched every change mentioning `A1SM02`, the final world 1 story mission. The hits all cancel open
buddy side quests when `A1SM02` is accepted (`patrols-set-mission-state`,
`common_buddysidequests.bsq_activate_and_cancel.lua@L147` and `mikesplace_bsqmanager_w1.lua@L245` on
`concurrent-missions`), so no world 1 quest is still running in world 2, where it would stop world 2's
quests from starting. That guards the mod's own concurrent missions rather than fixing a soft-lock on
assignment; it is the only candidate found in the data.
