---
title: A quad at Mike's bar instead of the Land Rover
kind: component
bundle: gameplay
status: located
systems: [world, vehicles]
match:
  - "levels/w1_c_3/generated/worldsectors/worldsector2680.data.fcb/**"
exclude: []
requires: [world-pala-guard-posts-removed]
verified: diff
---

# A quad at Mike's bar instead of the Land Rover

The Land Rover parked by Mike's bar in Pala is replaced by a DLC quad bike standing on the same
spot.

## How

`levels/w1_c_3` sector 2680, Mike's bar, 3 changes:

- `_layout.xml#delete[Land.Rover_21.2051812194420525687.xml]` removes the `vehicle.Land.Rover` at
  2620.72, 2121.86.
- A new fragment `land.dlc_vehicle1_dlc1_0.2057922578078523632.xml` places
  `vehicle.Land.DLC_Vehicle1_DLC1` (the quad, `selVehicleColor` `8`) at the Rover's exact position
  and angles, and `layer[main]/entity[land.dlc_vehicle1_dlc1_0]` lists it in the `main` layer, so
  it is there from the start.

The quad is the entity of the removed sector 3315 guard post, moved: same name and entity id
`2057922578078523632`, a `pgp_ai` vehicle before, a `main`-layer one now.

## Depends on

`world-pala-guard-posts-removed`, which deletes the quad from sector 3315; without it the same
entity id would be placed twice. The quad itself is the DLC library's vehicle, so the DLC content
must be installed.

## Uncertain

- The reason is not stated.
