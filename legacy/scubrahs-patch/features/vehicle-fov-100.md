---
title: Default vehicle field of view 100
kind: component
bundle: gameplay
claims:
  - "Increased default vehicle FOV to 100"
status: located
systems: [vehicles]
match:
  - "**/entitylibrary*.fcb/vehicle/**#**/FOV/fFOVAngle"
exclude: []
requires: [player-vehicle-override-copies, truck-engine-sounds]
existing: mods/UFCP — src/options/fov.cpp (Vehicle field of view option, at run time)
verified: diff
---

# Default vehicle field of view 100

Every drivable vehicle's camera opens at 100 degrees instead of 90 - the two DLC vehicles at 110.

## How

`CVehicle/FOV/fFOVAngle` `90` -> `100` on every vehicle archetype in `worlds/world1` (24),
`worlds/world2` (23) and the base game's `generated/entitylibrarypatchoverride.fcb` (63, the
multiplayer `.Multi` variants), and `90` -> `110` on the seven in `downloadcontent/dlc1`. The mod's
copies of eleven single-player vehicles in the override library carry `100` too.

Libraries load world, then override, then DLC, the later winning. So:

- the world-library edits to the eleven copied vehicles and to the Land Rover's `Multi_M249_Mounted`
  and `Multi_M2_Mounted` (which the base game's override library already declares) are dead - the
  copies, at the same `100`, are read instead;
- the DLC vehicles (`DLC_Vehicle1_DLC1`, `DLC_Vehicle2_DLC1` and their `Multi*` variants) are read
  from `downloadcontent/dlc1` at `110`; their world-library placeholders, their override-library
  multiplayer variants and the mod's override copies are dead.

25 of the 117 changes are dead that way.

## Depends on

`player-vehicle-override-copies` and `truck-engine-sounds` own the eleven copies (whole units).
