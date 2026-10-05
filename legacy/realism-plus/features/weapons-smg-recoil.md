---
title: MAC-10 and Uzi kick harder
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weapons/secondary/{mac10,uzi}.xml#**"
exclude: []
requires: []
verified: diff
---

# MAC-10 and Uzi kick harder

The two sub-machine guns climb and wander half as much again per shot.

## How

In the mod's copies of `weapons.Secondary.MAC10` and `weapons.Secondary.Uzi` in
`generated/entitylibrarypatchoverride.fcb`, every `CFCXWeapon/ReliabilityLevelsData/{Failure,Low,Medium,High}`
recoil value is multiplied by 1.5 (16 values):

- MAC-10: `fVerticalRecoilPerShot` `0.46/0.44/0.42/0.4` -> `0.69/0.66/0.63/0.6`,
  `fHorizontalRecoilPerShot` `0.26/0.24/0.22/0.2` -> `0.39/0.36/0.33/0.3`
- Uzi: `fVerticalRecoilPerShot` `0.68/0.66/0.64/0.62` -> `1.02/0.99/0.96/0.93`,
  `fHorizontalRecoilPerShot` `0.46/0.44/0.42/0.4` -> `0.69/0.66/0.63/0.6`

## Uncertain

- The paraphrased v2.1 list says SMG recoil was cut by half; the Final build raises it by half. The
  per-shot recoil reading of these fields is the community guide's
  ([weapons](../../../docs/docs/modding/guide/weapons.md#recoil)), not traced.
- The MAC-10's `Mikes_Rusty` weapon copy the mod adds carries the same values
  (`noise-weapons-editor-library-copies`).
