---
title: Convoy escorts are Unimogs
kind: component
bundle: gameplay
claims:
  - "Convoy vehicles are now escorted by Unimogs"
status: located
systems: [patrols, vehicles, missions]
match:
  - "**/entitylibrary*.fcb/ghostpatrols/convoy/escortvehicle.xml#Entity/Ghost/archVehicle"
exclude: []
requires: []
verified: diff
---

# Convoy escorts are Unimogs

The escort vehicles of convoy missions are the DLC Unimog instead of the Rover.

## How

`GhostPatrols.Convoy.EscortVehicle` in `worlds/world1` and `worlds/world2`
`entitylibrary.fcb/ghostpatrols/convoy/escortvehicle.xml`: `Ghost/archVehicle`
`vehicle.Land.Rover` -> `vehicle.Land.DLC_Vehicle2_DLC1` (the Unimog of the DLC pack, its model
`graphics\vehicles\land\unimog\unimog_dlc.xbg`).

The same archetype also gets a third crew member (`patrol-every-seat`) and mixed crew classes
(`patrol-diverse-weapons`); neither is needed for the swap.

## Uncertain

- `DLC_Vehicle2_DLC1` lives in `downloadcontent/dlc1/generated/entitylibrary.fcb`; that the
  escort spawns as intended on an install without that content loaded is not checked.
