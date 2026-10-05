---
title: Wider iron-sight field of view
kind: component
bundle: gameplay
claims:
  - "Slightly increased ADS FOV for many weapons"
status: located
systems: [weapons]
match:
  - "**/entitylibrary*.fcb/weaponproperties/**#**/fIronsightFOV"
exclude: []
requires: []
existing: mods/UFCP — src/options/fov.cpp (Ironsight field of view option, at run time)
verified: diff
---

# Wider iron-sight field of view

The AK-47, FAL, G3, MP5 and the mounted M2 zoom in a little less on their iron sights.

## How

`CWeaponProperties/CommonProperties/IronSight/fIronsightFOV` `0.95` -> `1` (radians, about 54 to 57
degrees) on eleven archetypes in each of `worlds/world1` and `worlds/world2`: `AK47`,
`AK47.AK47_Gold`, `FNFAL`, `FNFAL.Persistent`, `G3KA4`, `MP5`, `MP5.Mikes_Rusty`, `MP5.Persistent`,
`USAS12.Multi`, `M2_Mounted`, `M2_Mounted.Multi`. The value is the field of view the sights frame
the view at ([first-person aiming](../../../docs/docs/engine-internals/first-person-aiming.md)),
so a larger one is a wider view.

Six of the 22 edits are dead, because the override library, loaded after the world libraries,
declares those archetypes again:

- `AK47.AK47_Gold` (both worlds): the mod's own copy in `generated/entitylibrarypatchoverride.fcb`
  carries the same `1`; that whole unit belongs to `golden-ak47-shop`.
- `USAS12.Multi` and `M2_Mounted.Multi` (both worlds): the base game's override library has its own
  multiplayer copies, which the mod leaves at `0.95`.
