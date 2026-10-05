---
title: Lighter big trucks
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck{,/**}.xml#**/WheeledParams/fMass"
exclude: []
requires: []
verified: diff
---

# Lighter big trucks

The big truck weighs an eighth less, so it accelerates and handles less sluggishly.

## How

`CVehicleWheeledPhysComponent/WheeledParams/fMass` `4000` -> `3500` on `Land.BigTruck` and
`Land.BigTruck.A2LM09_NitrousTruck`, copies the mod adds to `generated/entitylibrarypatchoverride.fcb`
(redeclaring `worlds/world1` and `worlds/world2`). `Land.BigTruck.ScriptedBigTruck` keeps `4000`.
Boggalog's guide recommends cutting weight by at most 15 % to keep steering intact
([vehicles: weight](../../../docs/docs/modding/guide/vehicles.md#weight)).
