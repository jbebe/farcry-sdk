---
title: Shop stat bars match the new ballistics
kind: component
bundle: weapons
claims: []
status: located
systems: [economy, ui, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Summary/**"
exclude: []
requires: []
verified: diff
---

# Shop stat bars match the new ballistics

The arms dealer's damage, range, accuracy and reliability bars are redrawn to the mod's weapons:
battle rifles hit harder and reach further, pistols and SMGs are shown as short-ranged and less
accurate.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar/Summary/Weapons/Item[<weapon>]/Attributes/Attribute[<stat>]@value`,
27 values:

| Weapon | Damage | Range | Accuracy | Reliability |
|---|---|---|---|---|
| `fnfal` | 2 -> 3.5 | | | |
| `g3ka4` | 1.5 -> 3 | 2 -> 3 | | |
| `ak47` | | 2.5 -> 2.75 | 4 -> 3 | 4.5 -> 4 |
| `m16` | | 2.5 -> 3 | | |
| `mp5` | | | 4.5 -> 4 | |
| `ithaca` | | 1 -> 1.2 | 2.5 -> 2 | 4.5 -> 3 |
| `spas12` | | 0.5 -> 1 | | |
| `makarov` | | 1 -> 0.8 | 4.5 -> 1.8 | |
| `6p9` | | 1 -> 0.8 | 4.5 -> 3.5 | |
| `star45` | | 1.5 -> 1.15 | 4 -> 2 | |
| `deserteagle` | | | 3.5 -> 2.5 | |
| `mac10` | | 1.5 -> 1.85 | 4 -> 1.75 | 2 -> 1.75 |
| `uzi` | | | 3.5 -> 2 | |
| `pkm`, `m249` | | 1 -> 1.5 | | |
| `lpo50` | | 0.5 -> 0.65 | 4.5 -> 4 | |

## Depends on

Display only: the weapons themselves are the `weapons-*` pages (`weapons-fal-battle-rifle`,
`weapons-ballistics`, `weapons-flamethrower` and others).

## Uncertain

- Several bars (the G3's, AK's, Makarov's accuracy) have no matching weapon-property change in the
  mod's library; they describe the author's intent or the shooting feel rather than a value. Not a
  line of the readme.
