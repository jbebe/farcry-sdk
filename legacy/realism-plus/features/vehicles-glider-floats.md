---
title: A dropped hang glider floats on water
kind: component
bundle: fixes
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/air/paraglider{,/**}.xml#**/{fDiscardedMass,fUnderWaterMaxDepth}"
exclude: []
requires: []
verified: diff
---

# A dropped hang glider floats on water

A hang glider that lands in water settles on the surface, and can be stood on, instead of bouncing
off it.

## How

On `Air.Paraglider`, `Air.Paraglider.Paraglider_Lv3` and `Paraglider_Lv5`, copies the mod adds to
`generated/entitylibrarypatchoverride.fcb`:

- `CVehicleParagliderPhysComponent/fDiscardedMass` `75` -> `825` (the glider's mass once discarded);
- `CVehicle/fUnderWaterMaxDepth` `-1` -> `1.5`.

This is the "Hang gliders bouncing on water" fix of Boggalog's guide
([vehicles](../../../docs/docs/modding/guide/vehicles.md#bug-fix---hang-gliders-bouncing-on-water)).
