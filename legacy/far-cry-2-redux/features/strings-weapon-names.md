---
title: Real and shortened weapon names (English)
kind: component
bundle: ui
claims: []
status: located
systems: [weapons, ui]
match:
  - "languages/english/oasisstrings.fragment.xml#{Items,Challenges,StatisticService,WeaponBazaar}/*"
  - "languages/english/oasisstrings.fragment.xml#Tutorial/{WEAPONBAZAAR_*,COMPUTER_ADVERT_*}"
exclude: []
requires: []
verified: diff
---

# Real and shortened weapon names (English)

In English, weapons the base game gives invented names are called by their real ones, and long
names lose their "Rocket Launcher" or "Sniper Rifle" tail. Not a line of the readme.

## How

`languages/english/oasisstrings.fragment.xml`, every place a weapon is named: `Items`, the shop's
crate, operation-manual and repair-manual names (`WeaponBazaar` and their `Tutorial` duplicates),
`Challenges` and the statistics (`StatisticService` kills, headshots, executions):

| Base | Mod |
|---|---|
| AR-16 | M-16 |
| Homeland 37 | Ithaca 37 (`Items`, `Challenges` and statistics: Ithaca) |
| Eagle.50 | Desert Eagle |
| Star .45 | Star Model P |
| Silent MP-5 | MP5-SD |
| Silent Makarov 6P9 | Makarov 6P9 |
| FAL Paratrooper | FN FAL |
| Flare Pistol | Flare Gun |
| Carl G Rocket Launcher | Carl Gustaf Launcher |
| RPG-7 Rocket Launcher | RPG-7 |
| M-79 Grenade Launcher | M-79 |
| M1903 Sniper Rifle | M1903 |
| LPO-50 Flamethrower | LPO-50 |

The shop computer's adverts follow (`Tutorial/COMPUTER_ADVERT_01`, `_03`, `_05`: "the Makarov 6P9,
the MP5-SD", "The RPG-7 and", "the M-79", "A Flare Gun"). Other languages keep the base names.

## Uncertain

- `legacy/realism-plus` `strings-weapon-names` renames some of the same weapons in all ten
  languages with other spellings (M249, Desert Eagle, Ithaca 37); the two are independent.
