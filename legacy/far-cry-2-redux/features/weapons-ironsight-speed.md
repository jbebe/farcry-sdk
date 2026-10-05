---
title: Slower raise to the sights
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "**/weaponproperties/**#**/IronSight/fIronsightTransitionTime"
exclude:
  - "**/weaponproperties/special/lpo50/multi.xml#**"
requires: []
verified: diff
---

# Slower raise to the sights

Bringing any gun up to the sights takes a quarter of a second, the same for every weapon.

## How

`CommonProperties/IronSight/fIronsightTransitionTime` -> `0.25` (seconds) on the mod's copies in
`generated/entitylibrarypatchoverride.fcb` and the two DLC shotguns:

- from `0.2`: the AK-47 (and golden AK), FAL, G3KA4, MP5 (all three), MAC-10, Uzi, Star .45, flare
  gun (with `Flare_Gun_Merc`);
- from `0.15`: Ithaca, SPAS-12, USAS-12 (with `.Persistent`), M79, Makarov, 6P9, the sawed-off and
  silenced shotgun;
- the `.Multi` copies from `0.1` or `0.15`.

45 values with the `.Multi` copies; the field is the transition time of
[first-person aiming](../../../docs/docs/engine-internals/first-person-aiming.md).

## Depends on

Nothing. The enemies' flamethrower `LPO50.Multi` gets `0` instead (`weapons-enemy-flamethrower`).
The Ithaca's longer prepare delay is on `weapons-shotguns`.

## Uncertain

- Not a line of the readme. The sniper rifles, M16, machine guns and launchers keep their own times.
