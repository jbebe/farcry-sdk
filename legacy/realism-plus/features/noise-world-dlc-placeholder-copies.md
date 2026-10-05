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

1,161 dead changes that do nothing in game.

The world libraries declare `Land.DLC_Vehicle1_DLC1` and `Land.DLC_Vehicle2_DLC1` as placeholders
built on other vehicles (`datsun.xbg` and the Datsun's engine for the quad, `rover.xbg` and the Land
Rover's parts for the Unimog); the real quad and Unimog are in `downloadcontent/dlc1/generated/entitylibrary.fcb`.
The mod copies `worlds/world1`'s two placeholders into `generated/entitylibrarypatchoverride.fcb`
unchanged (only `fCameraRotationFactor` rounding differs). Compared with the DLC library's
declarations they override, the copies read as a wholesale rewrite: `datsun.xbg` in place of
`quad_single.xbg`, the Rover's parts, wheels, particles and `selVehicleColor` `6` in place of the
Unimog's `35`, other engine values.

The DLC library is read after the override library, so the game never reads these copies. The base
game's own multiplayer variants of the DLC vehicles in the override library
(`dlc_vehicle1_dlc1/multi.xml`, `dlc_vehicle2_dlc1/multi*.xml`), which the mod's tool rewrites with
new ids, a dropped `bIntelHackGliderOn` and other crush values, are shadowed the same way.

The mod's real DLC vehicle edits are in the DLC library and sit on `vehicles-faster-land` and
`vehicles-collision-damage`. The selVehicleColor change here is not the DLC colour feature, which
the mod makes with materials (see the graphics pages).
