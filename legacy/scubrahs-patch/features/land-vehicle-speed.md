---
title: Faster land vehicles
kind: component
bundle: balancing
claims:
  - "Increased speed for land vehicles"
status: located
systems: [vehicles]
match:
  - "**/entitylibrary*.fcb/vehicle/**#**/WheeledParams/{fEnginePower,fGearBoxTopSpeed}"
exclude: []
requires: []
verified: diff
---

# Faster land vehicles

Every wheeled vehicle has twice the engine power and twice the top speed.

## How

`CVehicleWheeledPhysComponent/WheeledParams/fEnginePower` and `fGearBoxTopSpeed` are doubled on
every wheeled archetype - `95` -> `190` and `31` -> `62` for the Land Rover, `150` -> `300` and `30`
-> `60` for the big truck, `88` -> `176` and `22` -> `44` for the DLC Unimog, and so on - in
`worlds/world1` (16 + 16), `worlds/world2` (14 + 14), the base game's
`generated/entitylibrarypatchoverride.fcb` multiplayer variants (36 + 36) and
`downloadcontent/dlc1` (7 + 7). Boats and the paraglider are not touched.

The mod's copies of the big trucks, the Jeep Liberty and Wrangler and the MK19 Land Rover in the
override library carry the same doubled values and are what the game reads for them; the DLC
vehicles are read from `downloadcontent/dlc1`, doubled there too. 40 of the 146 changes edit a copy
a later library replaces (the world-library versions of the copied vehicles, the override library's
DLC variants), so they are dead, with no difference in game.

## Depends on

The override library's vehicle copies are compared with the declarations they override, so each value is a separate change: this page, `vehicle-durability`, `land-vehicle-speed`, `vehicle-fov-100` and `truck-engine-sounds` (the big trucks' engine sound events) can each be picked alone.
