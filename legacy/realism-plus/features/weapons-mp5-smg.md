---
title: MP5 shares the SMG ammo pool
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/mp5{,/mikes_rusty,/persistent}.xml#**/{ammoAmmoType,text_ammoAmmoType}"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[{light_assault_webbing,assault_webbing}]/**"
exclude: []
requires: []
verified: diff
---

# MP5 shares the SMG ammo pool

The MP5 fires the MAC-10's and Uzi's ammunition instead of the assault rifles', and the light
assault webbing (the SMG ammo upgrade) now raises its reserve instead of the assault webbing.

## How

- `CommonProperties/Ammo/ammoAmmoType` `BC6782FC` (`assaultrifle`) -> `AA73EE0A` (`smg`), with its
  `text_ammoAmmoType` twin, on `WeaponProperties.Primary.MP5`, `.Mikes_Rusty` and `.Persistent` in
  `generated/entitylibrarypatchoverride.fcb`.
- `gamemodesconfig.xml` `BonusService`: the four `maxammo` bonuses for `object="mp5"`
  (`150/60/30/30` by difficulty) move from `Plan[assault_webbing]` (`bonus[0-3]` removed) to
  `Plan[light_assault_webbing]` (`bonus[+8..+11]` added), unchanged in value.

## Depends on

- The gun keeps `selCategory` `1`, so it still takes the primary slot. What the published line calls
  "the MP5 is a secondary" is otherwise only the shop: the bazaar lists it among the secondaries
  (`subcategory` `secondary`) and sells it from act 2 without an unlock, which is part of the
  shop rearrangement in `economy-bazaar-reorder`.
- Its longer effective range is `weapons-rifle-range`; its iron-sight view is `weapons-ironsight-fov`.
