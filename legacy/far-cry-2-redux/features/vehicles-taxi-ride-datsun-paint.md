---
title: The taxi ride's Datsun clean and repainted
kind: component
bundle: graphics
status: located
systems: [vehicles, graphics]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/datsun/scripteddatsun.xml#Entity/Components/CVehicle/{fDirtFactor,selVehicleColor}"
exclude: []
requires: []
verified: diff
---

# The taxi ride's Datsun clean and repainted

`Land.Datsun.ScriptedDatsun`, the Datsun placed in the opening taxi ride's mission layer
(`missions\openingsequence\taxiride`, `w1_d_3` sector 1724), loses its dirt and takes another body
colour.

## How

`CVehicle` on the copy in `generated/entitylibrarypatchoverride.fcb` (redeclaring `worlds/world1`):
`fDirtFactor` `0.15` -> `0`, `selVehicleColor` `9` -> `10`. Its handling changes are on
`vehicles-handling`.

## Uncertain

- Which colour index 10 is, and why only this Datsun changes, is not checked.
