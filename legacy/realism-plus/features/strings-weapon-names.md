---
title: Real weapon names
kind: component
bundle: weapons
status: located
systems: [weapons, ui]
match:
  - "languages/*/oasisstrings.fragment.xml#{StatisticService,Challenges,InGameEditor_Objects,Tutorial}/*"
  - "languages/*/oasisstrings.fragment.xml#Items/{deserteagle,g3ka4,ithaca,m249,m67,m79,mp5}"
  - "languages/*/oasisstrings.fragment.xml#WeaponBazaar/WEAPONBAZAAR_{DE,G3KA4,ITHACA,M249,M79,MP5}_*"
exclude: []
requires: []
verified: diff
---

# Real weapon names

Weapons the base game gives invented or hyphenated names are called by their real ones, and the
MP5 is described as a submachine gun.

## How

`languages/*/oasisstrings.fragment.xml`, in all ten languages, in every place a weapon is named -
`Items`, the shop's crate, operation-manual and repair-manual names (`WeaponBazaar`, and their
`Tutorial` duplicates), `Challenges`, the statistics (`StatisticService` kills, headshots,
executions, multi-kills), the map editor's object names (`InGameEditor_Objects`) and the shop
computer's adverts (`COMPUTER_ADVERT_01`, `_03`):

| Base | Mod |
|---|---|
| Homeland 37 | Ithaca 37 |
| Eagle.50 (Eagle .50) | Desert Eagle |
| G3-KA4 | G3KA4 |
| Silent MP-5 | Silent MP5 |
| M-79 Grenade Launcher | M79 Grenade Launcher |
| M-249 SAW (and M-249 in compound names) | M249 SAW |
| M-67 Grenade | M67 Grenade |

and, for the MP5 as a sidearm:

- `WeaponBazaar/WEAPONBAZAAR_MP5_CRATE_DESCRIPTION` "Assault" -> "SMG" (every language, the
  English word);
- `Tutorial/TUTORIAL_WEAPON_SECONDARY_2` "Machine Pistols" -> "Submachine Guns" (translated:
  "Mitraillettes", "Samopaly"...; not in German, Polish, Russian, Hungarian or Chinese, where the
  base word already fits).

Other languages translate around the names ("Lance-grenades M79", "Exécutions au Ithaca 37"). A
language skips an entry where its base text already reads right, so the counts differ (61 in
Hungarian to 76 in English, French and Italian; 726 in all).

## Depends on

- The MP5's move to the secondary slot and the shop reshuffle are on the weapons pages; these
  strings only label them.

## Uncertain

- The mod's notes also name a "Star .45 -> Colt 1911" rename; no string in the analysed archive
  changes the Star .45 (`WEAPONBAZAAR_STAR45_CRATE_NAME` stays "Star .45"). If it happens it is not
  in the string tables.
