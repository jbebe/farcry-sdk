---
title: Narrower hang glider view hides the arms' cut edges
kind: component
bundle: fixes
status: located
systems: [vehicles, graphics]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/air/paraglider{,/**}.xml#**/FOV/fFOVAngle"
exclude: []
requires: []
verified: diff
---

# Narrower hang glider view hides the arms' cut edges

Flying the hang glider, the player no longer sees where the first-person arms end.

## How

`CVehicle/FOV/fFOVAngle` `90` -> `81` on `Air.Paraglider`, `Air.Paraglider.Paraglider_Lv3` and
`Paraglider_Lv5`, copies the mod adds to `generated/entitylibrarypatchoverride.fcb`. This is the
"Seeing the edges of the player's arms when using hang gliders" fix of Boggalog's guide
([vehicles](../../../docs/docs/modding/guide/vehicles.md#bug-fix---seeing-the-edges-of-the-players-arms-when-using-hang-gliders)).

UFCP's field-of-view option lets the hang glider's own angle win over the player's setting
(`mods/UFCP/README.md`), so the two combine.
