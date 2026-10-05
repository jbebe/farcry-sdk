---
title: Sniper spawning inside a rock moved
kind: component
bundle: fixes
status: located
systems: [ai, world]
match:
  - "levels/w2_b_4/generated/worldsectors/worldsector4376.data.fcb/blue_faction.sniper_caucasian_5.*.xml#{hidPos,hidPos_precise,hidAngles}/*"
exclude: []
requires: []
verified: diff
---

# Sniper spawning inside a rock moved

One placed sniper in Bowa-Seko that spawned inside a rock now stands beside it.

## How

`Blue_Faction.Sniper_Caucasian_5` (`2055969661228432636`) in `w2_b_4` sector 4376:

- `hidPos` and `hidPos_precise` (3644.89, 3513.89, 43.163) -> (3624.21, 3491.94, 44.09), about 30 m
  across and a metre up;
- `hidAngles` (0, 0, 99) -> (-6.92, 5.77, -13.35), turned to face a different way and tilted
  slightly.

The same sniper is refiled into the Functional Outposts layer `missions\outposts\w2_b_4\manualc`;
that belongs to `outposts-functional`.

## Compared with Scubrah's Patch

[`sniper-in-rock`](../../scubrahs-patch/features/sniper-in-rock.md) fixes the same sniper with a
different spot, (3615.92, 3496.89, 43.64), and keeps his angles. The two fixes conflict per field;
pick one.

## Uncertain

- The tilt of a few degrees on x and y may come from placing him on a slope in an editor; whether
  the engine applies pitch and roll to a standing soldier is not checked.
