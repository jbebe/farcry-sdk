---
title: Truly random starting location
kind: component
bundle: gameplay
claims:
  - "Made the player's starting location truly random instead of being based on the location they \"died\" in Pala"
status: located
systems: [missions]
match:
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{809,823,837,851,1844}"
exclude: []
requires: []
verified: diff
---

# Truly random starting location

Where the player wakes up after collapsing during the escape from Pala is picked at random, not by
the quarter of town the player collapsed in.

## How

`domino/user/a1sm01_townescape.a1sm01_mission.lua` keeps the escape quadrant in
`self.QuadrantEscape`. Vanilla sets it from four `SetInteger` boxes, one per part of town the player
reaches. The mod replaces each of the four assignments with `random(1, 4)` (`@L809`, `@L823`,
`@L837`, `@L851`) and draws once more in `f_155_Out`, right before the value indexes the location list
(box `187`) (`@L1844`, logged as "TownEscape: Final quadrant").

## Uncertain

- That the quadrant picks the wake-up location is taken from the claim; the graph past
  `QuadrantEscape` was not traced.
