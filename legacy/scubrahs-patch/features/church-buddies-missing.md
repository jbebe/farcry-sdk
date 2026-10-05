---
title: Buddies go missing after helping Father Maliya
kind: component
bundle: fixes
claims:
  - "Fixed an issue where buddies wouldn't be considered \"missing\" if the player chose to help father Maliya at the church in town"
status: located
systems: [missions, buddies]
match:
  - "domino/user/a1sm03_defensereversal.churchassault.lua@L237"
exclude: []
requires: []
verified: diff
---

# Buddies go missing after helping Father Maliya

If the player sides with Father Maliya at the church during the Act 1 defence reversal, the buddies
are now marked missing as on the other branch, so they can turn up in the betrayal sequence.

## How

`domino/user/a1sm03_defensereversal.churchassault.lua@L237` inserts a call to
`GetBuddiesManager():SetDefenceRevesalBetrayedBuddies()`, commented "Update 3.6: Set buddies status
to missing to allow them to spawn during the betrayal segment".

## Uncertain

- The `A1SM03_MikesDefenseMarker_*` entities in `mapsdata`, despite their name, are buddy side-quest
  markers and belong to `buddy-quests-complete-on-objective` and `concurrent-missions`.
