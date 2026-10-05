---
title: Pistols hit harder, reach further and carry more ammo
kind: component
bundle: weapons
status: located
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/{makarov,silencedmakarov_6p9,star45,deserteagle}{,/persistent}.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/weapons/secondary/deserteagle.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[pistol_belt]/**"
exclude:
  - "**#**/fIronsightFOV"
requires: []
verified: diff
---

# Pistols hit harder, reach further and carry more ammo

The Makarov, the silenced Makarov (6P9), the Star .45 and the Desert Eagle do more damage per shot;
the Makarovs take a bigger magazine and, with the Star .45, up to three times the reserve ammo. The Desert
Eagle kicks twice as hard.

## How

In the mod's copies in `generated/entitylibrarypatchoverride.fcb`, each compared with the world
library declaration it overrides:

| Weapon properties | Change |
|---|---|
| `Secondary.Makarov`, `Secondary.SilencedMakarov_6P9` | `Stim_ImpactDamage/nLevel` `8` -> `10`; `iAmmoInClip` `8` -> `12`; `iMaxAmmoCasual/Experimented/Hardcore/Infamous` `40/24/24/16` -> `120/72/48/24`; iron-sight spread `BulletSpread_IronSight/fAmplitude` `0.3` -> `0.35`, `BulletSpreadCrouch_IronSight/fAmplitude` `0.28` -> `0.32` |
| `Secondary.Star45` | `nLevel` `10` -> `14`; `iMaxAmmo*` `40/24/24/16` -> `120/72/48/24`; `vectorEffectiveRange` `30/40` -> `40/55`, `vectorEffectiveRangeIS` `40/50` -> `55/65` |
| `Secondary.DesertEagle`, `.Persistent` | `nLevel` `16` -> `24`; `vectorEffectiveRange` `199.5/199.5` -> `30/40` |

- `weapons.Secondary.DesertEagle`: `CFCXWeapon/ReliabilityLevelsData/{Failure,Low,Medium,High}/fVerticalRecoilPerShot`
  doubled, `2.8/2.7/2.6/2.5` -> `5.6/5.4/5.2/5`.
- `gamemodesconfig.xml` `BonusService/Plan[pistol_belt]`: the pistol belt's extra ammo for the
  Makarov, Star .45 and 6P9 triples to match, `40/16/8/8` -> `120/48/24/24` per difficulty (12
  values). The Desert Eagle's belt bonus is left at `40/16/8/8`.

## Depends on

Nothing. The pistols' slightly wider iron-sight view (`fIronsightFOV` `1.309` -> `1.3`) is
`weapons-ironsight-fov`.

## Uncertain

- The Desert Eagle's effective range drops: the base game's `199.5` looks like a placeholder, and
  `30/40` puts it beside the Star .45. The `.Persistent` copy of the Desert Eagle's weapon entity the
  mod adds is a copy of this one with the doubled recoil (`noise-weapons-editor-library-copies`).
- The published summary speaks of 0.75x Desert Eagle ammo; no Desert Eagle ammo value changes.
