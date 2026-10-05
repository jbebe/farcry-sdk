---
title: Re-balanced weapon and equipment prices
kind: component
bundle: balancing
claims:
  - "Re-balanced all weapon and equipment costs"
status: located
systems: [economy]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[*]@cost"
exclude: []
requires: []
verified: diff
---

# Re-balanced weapon and equipment prices

Almost everything sold at the weapons bazaar costs more, most weapons two to four times the vanilla
price.

## How

Only the `cost` attribute of the existing `WeaponBazaar/Item` entries in
`engine/gamemodes/gamemodesconfig.xml` changes - 98 items. Representative values, old -> new:

- weapon crates: Makarov `4 -> 5`, Ithaca `4 -> 15`, AK-47 `10 -> 40`, FAL `10 -> 40`, M16 `20 -> 50`,
  Dragunov `20 -> 50`, AS50 `35 -> 55`, dart rifle `10 -> 50`, PKM `10 -> 40`, M79 `20 -> 50`,
  IED `10 -> 35`, flare gun `4 -> 10`, silenced Makarov `6 -> 35`
- operation and repair manuals: mostly `1-8 -> 5-15`; vehicle repair manuals `10-15 -> 20`
- ammo bags (pistol belt, webbings, bandoliers, satchels, gunner pack): `7-15 -> 20-35`
- camo suit `45 -> 50`, first aid manuals `25 -> 50`
- the three "magic" weapon crates (primary/secondary/special): `16/8/24 -> 25`

The prices of the items the mod adds (golden AK-47, the DLC weapons, the GPS range upgrade) sit
inside those items' own entries and belong to their pages.

## Depends on

Nothing. The same patch raises mission income separately (`economy-mission-rewards`,
`assassinations-20-diamonds`, `buddy-missions-20-diamonds`). `economy-diamond-counter` hard-codes
these new prices in its script; picking it without this page makes its count drift from the real
wallet.
