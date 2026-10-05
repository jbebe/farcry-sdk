---
title: Golden AK-47 recoil halved
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weapons/primary/ak47/ak47_gold.xml#**"
exclude: []
requires: []
verified: diff
---

# Golden AK-47 recoil halved

The golden AK-47 climbs half as much per shot as before.

## How

`weapons.Primary.AK47.AK47_gold`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`:
`CFCXWeapon/ReliabilityLevelsData/{Failure,Low,Medium,High}/fVerticalRecoilPerShot`
`1.7/1.5/1.3/1.1` -> `0.85/0.75/0.65/0.55`.

## Depends on

Nothing. The golden AK's range and sight changes are `weapons-rifle-range` and
`weapons-ironsight-fov`; enemies carry it rarely through `weapons-enemy-loadouts`.
