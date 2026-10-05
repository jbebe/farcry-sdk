---
title: Flamethrower reaches three times as far on half the fuel
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50{,/persistent}.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[{lpo50_oper_manual,lpo50_repair_manual}]@object"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[pyrotechnic_satchel]/**"
exclude:
  - "**#**/fForcedReliability"
requires: []
verified: diff
---

# Flamethrower reaches three times as far on half the fuel

The LPO-50 throws a flame three times the size at twice the speed, and carries half the fuel, its
tank and its upgrades included.

## How

`WeaponProperties.Special.LPO50` and `.Persistent`, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`:

- `FireStrategyProperties/FlameMesh/fSize` `10` -> `30`, `FlameMesh/fSpeed` `15` -> `30`
- `iAmmoInClip` `200` -> `100`; `iMaxAmmoCasual/Experimented/Hardcore/Infamous` `500/300/300/200`
  -> `250/150/150/100`
- `CommonProperties/sName` `lpo50` -> `lpo50new`

`gamemodesconfig.xml` `BonusService`, following the new name:

- `Plan[lpo50_oper_manual]@object` and `Plan[lpo50_repair_manual]@object` `lpo50` -> `lpo50new`
- `Plan[pyrotechnic_satchel]` `bonus[4-7]`: `object` `lpo50` -> `lpo50new`, `value`
  `500/200/100/100` -> `250/100/50/50`

## Depends on

- The renamed `sName` is what the bonus plans key on; picking the weapon values without the
  `BonusService` lines leaves the manuals and the satchel pointing at a name no weapon has
  (inference from the matching rename).
- The enemies' flamethrower is a different archetype, `LPO50.Multi`, tuned in
  `weapons-enemy-loadouts`. The `.Persistent` reliability change is
  `weapons-persistent-reliability`.

## Uncertain

- `fSize` as the flame's reach (the paraphrased v2.1 list's "range x3, to 30 m") is the guide's
  reading ([weapons](../../../docs/docs/modding/guide/weapons.md#flamethrower-range)); not traced.
- Why the name changes is not known. `gamemodesconfig.xml` still names `lpo50` in
  `Plan[lpo50_challenge_bonus]@object`, the bazaar summary `Item[lpo50]` and a `Weapon name="lpo50"`
  code entry, which the rename leaves behind; whether the challenge bonus still reaches the
  flamethrower is not checked.
