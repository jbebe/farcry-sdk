---
title: Dead copies of the world library's DLC vehicle placeholders
kind: noise
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
exclude: []
requires: []
verified: diff
---

# Dead copies of the world library's DLC vehicle placeholders

1,154 dead changes that do nothing in game.

The world libraries declare `Land.DLC_Vehicle1_DLC1` (the quad) and `Land.DLC_Vehicle2_DLC1` (the
Unimog) as placeholders built on other vehicles: the Datsun's model and parts for the quad, the
Land Rover's for the Unimog. The real quad and Unimog are in
`downloadcontent/dlc1/generated/entitylibrary.fcb`, which is read after the override library. The
mod copies `worlds/world1`'s two placeholders into `generated/entitylibrarypatchoverride.fcb`, and
compared with the DLC declarations they redeclare, the copies read as a wholesale rewrite
(`datsun.xbg` for `quad_single.xbg`, Rover parts and wheels, other engine values): 544 changes on
the quad, 610 on the Unimog.

Compared with the world-library placeholders themselves, the mod did edit them, as if they were
the cars they are built on:

- the quad copy takes the broken and scripted Datsuns' handling of `vehicles-handling` (mass 900,
  top speed 45, the 36/6/8/6/8/6 steering, 1500 brakes, the gear emulation), `selVehicleColor`
  `5` -> `10`, wheel-roll sounds `0x004EEA82`-`0x004EEA85` and the `Compatible.In_Vehicles` mix
  preset;
- the Unimog copy takes the Land Rover's `Gear2/fMaxSpeed` `15` -> `35`.

None of it reaches the game, which reads the DLC library's declarations. The patrols that now use
the quad and Unimog (`patrols-vehicle-mix`) get the DLC library's vehicles with vanilla values.
Realism Plus ships the same dead copies
([`noise-world-dlc-placeholder-copies`](../../realism-plus/features/noise-world-dlc-placeholder-copies.md)).
