---
title: PKM recoil cut by a fifth
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weapons/special/pkm{,/pkm_merc}.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/pkm{,/mikes_rusty,/pkm_merc}.xml#**/iRecoilRecoveryLevel"
exclude: []
requires: []
verified: diff
---

# PKM recoil cut by a fifth

The PKM climbs a fifth less per shot and recovers like every other gun.

## How

In `generated/entitylibrarypatchoverride.fcb` (the mod's copies):

- `weapons.Special.PKM` and `weapons.Special.PKM.PKM_Merc` (the enemies' copy):
  `CFCXWeapon/ReliabilityLevelsData/{Failure,Low,Medium,High}/fVerticalRecoilPerShot`
  `1.81/1.79/1.77/1.75` -> `1.46/1.44/1.42/1.4`.
- `WeaponProperties.Special.PKM`, `.Mikes_Rusty` and `.PKM_Merc`: `CommonProperties/Recoil/iRecoilRecoveryLevel`
  `2` -> `1`, the value every other weapon has; the guide recommends exactly this
  ([weapons](../../../docs/docs/modding/guide/weapons.md#recoil-recovery)).

## Uncertain

- The `weapons.Special.PKM.Mikes_Rusty` entity the mod adds is a copy of the PKM with the cut recoil
  (`noise-weapons-editor-library-copies`).
