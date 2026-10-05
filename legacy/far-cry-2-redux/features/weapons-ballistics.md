---
title: Range, damage and ammo tweaks to single weapons
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/as50.xml#**/ImpactStims/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/deserteagle.xml#**/ImpactStims/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/silencedmakarov_6p9.xml#**/CommonProperties/{bIsSilent,fRange,iClipsForSelfDestruct}"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/mac10.xml#**/CommonProperties/fRange"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/dart_rifle.xml#**/CommonProperties/fRange"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/dart_rifle.xml#**/Ammo/iMaxAmmo*"
exclude: []
requires: []
verified: diff
---

# Range, damage and ammo tweaks to single weapons

A handful of per-weapon corrections in the spirit of the description's "more accurate range and
damage": heavier impacts for the AS50 and Desert Eagle, shorter range for the pistol-calibre guns,
a silent 6P9, and the same dart supply on every difficulty.

## How

The armory copies in `generated/entitylibrarypatchoverride.fcb` (the `.Persistent`, `_Merc` and
`.Multi` copies are not changed):

| Weapon properties | Field | From | To |
|---|---|---|---|
| `Primary.AS50` | `ImpactStims/Stims/Stim[0]/nLevel` | `6` | `8` |
| `Secondary.DesertEagle` | `ImpactStims/Stims/Stim[0]/nLevel` | `6` | `7` |
| `Secondary.SilencedMakarov_6P9` | `CommonProperties/bIsSilent` | `False` | `True` |
| | `fRange` | `100` | `90` |
| | `iClipsForSelfDestruct` | `25` | `24` |
| `Secondary.MAC10` | `fRange` | `200` | `160` |
| `Special.Dart_Rifle` | `fRange` | `400` | `300` |
| | `Ammo/iMaxAmmoCasual/Experimented/Hardcore/Infamous` | `9/4/3/2` | `5/5/5/5` |

## Depends on

Nothing. The FAL's and MP5's larger reworks are `weapons-fal-battle-rifle` and `weapons-mp5-sd`.

## Uncertain

- `ImpactStims` levels are the impact effect (penetration and knock-back stim) rather than the
  damage stim; their exact effect is not traced. Not a line of the readme.
