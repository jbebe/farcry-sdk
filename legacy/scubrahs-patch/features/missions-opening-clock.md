---
title: Morning start for the opening missions
kind: component
status: located
systems: [missions, environment]
match:
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L2000"
  - "domino/user/a1bu00_tutorial.a1bu00_storymission.lua@L1413"
exclude: []
requires: []
verified: diff
---

# Morning start for the opening missions

The player wakes in the Pala hotel at 9:00 instead of 11:00, and the tutorial that follows starts at
9:20 instead of 14:00. Not on the published list.

## How

- `domino/user/a1sm01_townescape.a1sm01_mission.lua@L2000`: the `SetTimeOfDay` box fired as the hotel
  wake-up begins (after the `A1SM01_FanTurning` sequence stops, before the `whitescreenfx` fade is
  removed) gets `Hour` `11` -> `9` (`Minute` stays `0`).
- `domino/user/a1bu00_tutorial.a1bu00_storymission.lua@L1413`: the tutorial's `SetTimeOfDay` gets
  `Hour` `14` -> `9`, `Minute` `0` -> `20`.

## Uncertain

- The reason is not stated. The two values read as one consistent morning (9:00, then 9:20); the
  morning fog of `morning-fog` only runs from 5:00 to 8:00, so it is not to show the fog.
