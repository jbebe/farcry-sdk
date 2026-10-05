---
title: Mortar carries more rounds
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/mortar{,/mortar_merc,/persistent}.xml#**/iMaxAmmo*"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[rocketeer_satchel]/bonus[{8,9,10}]@value"
exclude: []
requires: []
verified: diff
---

# Mortar carries more rounds

The mortar holds half as many rounds again, and the rocketeer satchel adds more on top.

## How

- `WeaponProperties.Special.Mortar`, `.Mortar_Merc` and `.Persistent`, the mod's copies in
  `generated/entitylibrarypatchoverride.fcb`: `iMaxAmmoCasual/Experimented/Hardcore/Infamous`
  `4/2/2/1` -> `6/3/3/2`.
- `gamemodesconfig.xml` `BonusService/Plan[rocketeer_satchel]`: the mortar's bonuses
  (`bonus[8-10]`) `3/2/1` -> `5/3/2` for Casual, Experienced and Hardcore; Infamous stays `1`.

## Depends on

Nothing. The `.Persistent` mortar's reliability change is `weapons-persistent-reliability`.
