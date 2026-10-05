---
title: Assault rifles and the MP5 reach a third further
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/{ak47,fnfal,g3ka4,m16,mp5}{,/*}.xml#**/vectorEffectiveRange{,IS}/*"
exclude:
  - "**/multi.xml#**"
requires: []
verified: diff
---

# Assault rifles and the MP5 reach a third further

The AK-47 (and the golden AK-47), FAL, G3KA4, M16 and MP5 keep full effect a third further out.

## How

`CommonProperties/vectorEffectiveRange` `45/60` -> `60/80` and `vectorEffectiveRangeIS` (aimed)
`60/75` -> `80/100` on `WeaponProperties.Primary.AK47`, `AK47.AK47_Gold`, `FNFAL`,
`FNFAL.Persistent`, `G3KA4`, `M16`, `M16.Persistent`, `MP5`, `MP5.Mikes_Rusty` and `MP5.Persistent`
(40 values), the mod's copies in `generated/entitylibrarypatchoverride.fcb`.

## Uncertain

- Read as the two ends of the damage fall-off range (hip, then aimed); the fall-off itself is not
  traced. The paraphrased v2.1 list gives the MP5 a shorter range; in the Final build it gets the
  rifles' longer one.
