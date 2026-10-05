---
title: Ammo upgrades cover the DLC weapons
kind: component
bundle: balancing
claims:
  - "Ammo upgrade purchases now affect DLC weapons"
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[{shotgun_bandolier,rocketeer_satchel}]/bonus[+*]"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/dlc1/{sawedoffshotgun,silencedshotgun}.xml#**/iMaxAmmo*"
exclude: []
requires: []
verified: diff
---

# Ammo upgrades cover the DLC weapons

Buying the shotgun bandolier now raises the carried ammo of the silenced and sawed-off shotguns, and
the rocketeer satchel that of the explosive crossbow. Both shotguns also start from the vanilla
shotguns' ammo capacity.

## How

- `engine/gamemodes/gamemodesconfig.xml`, `BonusService`: twelve new `<bonus attr="maxammo">`
  entries, each copying the numbers vanilla gives the weapons already in that plan:
  - `Plan[shotgun_bandolier]`: `object="silencedshotgun"` and `object="sawedoffshotgun"`, `value`
    `60/24/12/12` at `difficultyLevel` `0/1/2/3` (as the Ithaca, SPAS-12, USAS-12)
  - `Plan[rocketeer_satchel]`: `object="crossbow"`, `3/2/1/1` (as the RPG-7, Carl Gustaf, mortar)
- `downloadcontent/dlc1` weapon properties `sawedoffshotgun` and `silencedshotgun`,
  `CommonProperties/Ammo/iMaxAmmoCasual/Experimented/Hardcore/Infamous`: sawed-off `71/47/47/35 ->
  60/36/36/24`, silenced `126/66/54/42 -> 60/36/36/24` - the vanilla SPAS-12's capacities, the base
  the bandolier bonus is added to.

The golden AK-47's assault-webbing bonus is part of `golden-ak47-shop`.

## Depends on

Nothing to work. The sawed-off's switch from pistol to shotgun ammo (`ammoAmmoType`, its own fix
`sawed-off-shotgun-ammo`) is what puts that gun in the shotgun pool these numbers belong to.
