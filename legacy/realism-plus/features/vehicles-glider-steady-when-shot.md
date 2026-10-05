---
title: Hang glider no longer drops out of the sky when shot
kind: component
bundle: fixes
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/air/paraglider{,/**}.xml#**/ParagliderParams/fMass"
exclude: []
requires: []
verified: diff
---

# Hang glider no longer drops out of the sky when shot

Gunfire still jolts the hang glider, but it only falls under heavy fire instead of tumbling or
looping at the first hit.

## How

`CVehicleParagliderPhysComponent/ParagliderParams/fMass` `300` -> `2420` on `Air.Paraglider`,
`Air.Paraglider.Paraglider_Lv3` and `Paraglider_Lv5`, copies the mod adds to
`generated/entitylibrarypatchoverride.fcb` (redeclaring `worlds/world1` or `worlds/world2`). This is
the "Hang gliders falling out of the sky when shot" fix of Boggalog's guide, whose author found
`2420` by testing
([vehicles](../../../docs/docs/modding/guide/vehicles.md#bug-fix---hang-gliders-falling-out-of-the-sky-when-shot)).

The mod also adds `Paraglider_Lv1`, `Lv2` and `Lv4` from the editor template with the same value;
nothing in the campaign places them (`noise-world-template-archetypes`).
