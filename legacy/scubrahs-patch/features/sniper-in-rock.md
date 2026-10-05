---
title: Sniper spawning inside a rock
kind: component
bundle: fixes
claims:
  - "Fixed an enemy sniper that would spawn inside a rock"
status: located
systems: [ai, world]
match:
  - "levels/w2_b_4/generated/worldsectors/worldsector4376.data.fcb/blue_faction.sniper_caucasian_5.*.xml#{hidPos,hidPos_precise}/*"
exclude: []
requires: []
verified: diff
---

# Sniper spawning inside a rock

One placed sniper in Bowa-Seko is moved out of the rock it spawned in.

## How

`Blue_Faction.Sniper_Caucasian_5` (`2055969661228432636`) in `w2_b_4` sector 4376: `hidPos` and
`hidPos_precise` go from (3644.89, 3513.89, 43.163) to (3615.92, 3496.89, 43.64), about 34 m
across and half a metre up. It is the only placed entity in the mod whose position changes.

The same sniper is also refiled into the Functional Outposts layer `missions\outposts\w2_b_4\manualc`;
that move belongs to `functional-outposts` and is independent of this fix.
