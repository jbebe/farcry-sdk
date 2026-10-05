---
title: Multiplayer vehicle variants' crush threshold raised
kind: component
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/**#**/EngineExplosion/*"
exclude:
  # the dead placeholder copies of the DLC vehicles
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
requires: []
verified: diff
---

# Multiplayer vehicle variants' crush threshold raised

The multiplayer variants of the Datsun, Jeeps and Land Rovers get a higher instant-explosion crush
value. Two of them stand in the campaign.

## How

`CVehicle/EngineExplosion/nInstantExplosionCrushMaxHealth` `22` -> `23` (`Land.Datsun.Multi` and its
`APR`/`Neutral`/`UFLL` children), `24` (`Land.JeepWrangler.Multi` and children), `25`
(`Land.JeepLiberty.Multi` and children) and `26` (`Land.Rover.Multi`, `Multi_M2_Mounted`,
`Multi_M249_Mounted`, `Multi_MK19_Mounted` and their children): 23 values on the base game's
multiplayer variants in `generated/entitylibrarypatchoverride.fcb`, which the mod rewrites (see
`noise-world-vehicle-multi-reexport`). The campaign vehicles keep `28`.

The campaign places two of these: a `Land.Rover.Multi_M249_Mounted` in `w2_c_4` sector 3653 and a
`Land.Rover.Multi_M2_Mounted` in sector 3655. The rest are multiplayer-only.

## Uncertain

- What the value does (presumably the health below which a crush makes the vehicle explode at once)
  is read from its name, not traced.
- Whether this is a deliberate edit or a by-product of how the mod's tool rewrote the variants is
  not known; no published line mentions it.
