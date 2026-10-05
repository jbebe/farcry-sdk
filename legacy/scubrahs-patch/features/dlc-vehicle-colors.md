---
title: DLC quad colors randomized
kind: component
bundle: visuals
claims:
  - "DLC vehicle colors are now randomized"
status: located
systems: [vehicles, graphics]
match:
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/vehicle/**#**/selVehicleColor"
  - "graphics/_materials/sdore2-m-*.xbm"
exclude: []
requires: []
verified: diff
---

# DLC quad colors randomized

The DLC quad no longer always spawns in one paint scheme.

## How

`vehicle.Land.DLC_Vehicle1_DLC1` (the quad, `quad_single.xbg`) in
`downloadcontent/dlc1/generated/entitylibrary.fcb/vehicle/land/dlc_vehicle1_dlc1.xml`:
`CVehicle/selVehicleColor` `5` ("Datsun Brown") -> `27` ("APR Buggy"). The archetype's
`nMinRandomColorIndex` `5` and `nMaxRandomColorIndex` `10` are unchanged. Its multiplayer variant
`dlc_vehicle1_dlc1/multi.xml` already uses `27`, and the Unimog (`DLC_Vehicle2_DLC1`) uses `35`
("UFLL Datsun"), both untouched.

## Uncertain

- That a faction color index makes the engine pick a random color between `nMinRandomColorIndex`
  and `nMaxRandomColorIndex` is an inference from the claim and from the Unimog already using one;
  the engine's color pick is not traced. Only the quad changes, although the claim says "vehicles".
- The mod also ships a new copy of the same archetype in
  `generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle1_dlc1.xml` (a whole new unit,
  not on this page) that keeps `selVehicleColor` `5`. If the patch override wins over the DLC
  library at load, this change does nothing; which library wins is not traced.
