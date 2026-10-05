---
title: Tougher vehicles
kind: component
bundle: balancing
claims:
  - "Increased the amount of damage that vehicles can withstand"
status: located
systems: [vehicles]
match:
  - "**/entitylibrary*.fcb/vehicle/**#**/fHealth"
  - "**/entitylibrary*.fcb/vehicle/**#**/fInitialReliability"
exclude: []
requires: [player-vehicle-override-copies, truck-engine-sounds]
verified: diff
---

# Tougher vehicles

Every campaign vehicle part has at least twice its health, and vehicles start out more reliable, so
they take longer to wreck and to break down. The two DLC vehicles only gain reliability.

## How

- `Parts/Part[*]/fHealth` on every wheeled, floating (boat) and paraglider archetype, mostly doubled
  (`50` -> `100`, `500` -> `1000`, `700` -> `1400`), some parts quadrupled (`200` -> `800`, `400` ->
  `1600`) or raised less (`4000` -> `6000`, `300` -> `400`).
- `Reliability/fInitialReliability` `1` -> `2` on the same archetypes (`1` -> `1.5` in
  `downloadcontent/dlc1`).
- The two burnt-out car wrecks (`vehicle/wreck/carburned01_bk`, `carwrecked01_bk`): their
  `CCompoundPhysComponent` node `fHealth` `100` -> `200`.

Spread over `worlds/world1` (246 + 23), `worlds/world2` (214 + 22), the base game's
`generated/entitylibrarypatchoverride.fcb` multiplayer variants (525 + 63) and
`downloadcontent/dlc1` (7 reliability values, no health).

296 of the 1,100 changes are dead, edits to a copy a later library replaces:

- The world-library versions of the eleven vehicles the mod copies into the override library are
  replaced by those copies, whose health values do not always match (big truck part 2: `1600` in
  the copy, `3200` in `world1`; MK19 Land Rover part 1: `8000` against `16000` in `world1`).
- The DLC vehicles are read from `downloadcontent/dlc1`, which raises only their reliability: the
  doubled and quadrupled part health in the override library's DLC variants and in the mod's DLC
  copies never reaches the game.

## Depends on

`player-vehicle-override-copies` and `truck-engine-sounds` own the copies (whole units).
