---
title: Multiplayer vehicle variants renumbered by the mod's tool
kind: noise
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/**#Entity/disEntityId"
  - "generated/entitylibrarypatchoverride.fcb/vehicle/**#Entity/Components/CGraphicComponent/bIntelHackGliderOn"
exclude:
  # the dead placeholder copies of the DLC vehicles
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
requires: []
verified: diff
---

# Multiplayer vehicle variants renumbered by the mod's tool

Not an edit: the base game's override library holds the multiplayer variants of every vehicle
(`*.Multi`, `*.Multi.APR`, `.Neutral`, `.UFLL`, the `Multi_*_Mounted` guns), and the mod's tool wrote
them back with two by-products, 117 changes:

- `Entity/disEntityId` renumbered, 59 values shifted down by five (`5356` -> `5351` for
  `Air.Paraglider.Multi`). The game finds archetypes by name, so the id does nothing (inference, as
  for Scubrah's Patch's machete copies).
- `CGraphicComponent/bIntelHackGliderOn` (`False`) dropped from 58 of them. The same field is dropped
  from the override library's weapons, pickups and gadgets; a missing `False` flag reads as `False`
  (inference).

The variants' one changed value, `nInstantExplosionCrushMaxHealth`, is on
`vehicles-multi-explosion-crush`.
