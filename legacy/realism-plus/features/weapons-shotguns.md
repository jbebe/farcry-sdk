---
title: Shotguns reach further and spread tighter
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/{ithaca,spas12,usas12}{,/persistent}.xml#**"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/silencedshotgun.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[shotgun_bandolier]/bonus[{4,5,6,7}]@value"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[shotgun_bandolier]/bonus[{+12,+13,+14,+15}]"
exclude:
  - "**#**/{fIronsightFOV,fForcedReliability}"
requires: []
verified: diff
---

# Shotguns reach further and spread tighter

The Ithaca, SPAS-12 and the Fortune's Pack silenced shotgun throw a tighter pattern and keep their
effect much further out. The SPAS-12 loads nine shells instead of twelve; the USAS-12 gains range but
spreads wider.

## How

In `generated/entitylibrarypatchoverride.fcb` (the mod's copies) and, for the silenced shotgun,
`downloadcontent/dlc1/generated/entitylibrary.fcb`:

| Weapon properties | Effective range (hip x/y, aimed x/y) | Spread | Other |
|---|---|---|---|
| `Primary.Ithaca` | `15/20, 20/25` -> `25/40, 40/50` | `fAngleYaw/PitchBulletSpread` `2` -> `1.5` | |
| `Primary.SPAS12`, `.Persistent` | `15/20, 20/25` -> `25/35, 35/40` | `fSecondaryAngleYaw/PitchBulletSpread` `7` -> `4` | `iAmmoInClip` `12` -> `9` (base copy); `iMaxAmmoCasual` `60` -> `63` (base copy) or `24` -> `63` (`.Persistent`); `.Persistent` `iMaxAmmoExperimented/Hardcore` `24` -> `36`; `iMaxAmmoInfamous` `24` -> `27`; `Stim_ImpactDamage/nLevel` `17` -> `18` |
| `Primary.USAS12`, `.Persistent` | `15/20, 20/25` -> `20/25, 25/30` | primary `2` -> `3`, secondary `5` -> `6` (wider) | |
| `DLC1.SilencedShotgun` | hip `y` `20` -> `30`, aimed `20/25` -> `30/40` | primary `2` -> `1.6`, secondary `3` -> `3.2` | `iAmmoInClip` `6` -> `4`; `iMaxAmmo*` `126/66/54/42` -> `68/44/44/32`; `nLevel` `20` -> `17`; `iClipsForSelfDestruct` `28` -> `34` |

`gamemodesconfig.xml` `BonusService/Plan[shotgun_bandolier]`:

- the SPAS-12's bandolier bonus (`bonus[4-7]`) `60/24/12/12` -> `54/27/9/9`, whole magazines of nine;
- four new `maxammo` bonuses for `object="silencedshotgun"`, `60/24/12/12` by difficulty
  (`bonus[+12..+15]`), so the bandolier covers the DLC gun.

## Depends on

Nothing. Scubrah's Patch adds the same silenced-shotgun bandolier bonuses
([`dlc-ammo-upgrades`](../../scubrahs-patch/features/dlc-ammo-upgrades.md)). The sawed-off is
`weapons-sawed-off`; the shotguns' iron-sight view is `weapons-ironsight-fov`; the
`USAS12.Persistent` reliability change is `weapons-persistent-reliability`.

## Uncertain

- `fAngle*BulletSpread` and `fSecondaryAngle*BulletSpread` read as the pellet cone (hip and aimed
  or primary and secondary pattern); which is which is not traced.
