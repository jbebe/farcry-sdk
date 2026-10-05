---
title: Bigger ammo upgrades on the harder difficulties
kind: component
bundle: gameplay
claims: []
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/**"
exclude: []
requires: []
verified: diff
---

# Bigger ammo upgrades on the harder difficulties

The ammo pouches and satchels bought at the arms dealer add nearly as much on Normal, Hardcore and
Infamous as on Casual, the grenadier webbing also adds crossbow bolts, and the gunner pack adds less
on Casual.

## How

`engine/gamemodes/gamemodesconfig.xml`, `BonusService`, the `maxammo` bonuses each upgrade grants
per difficulty (Casual/Normal/Hardcore/Infamous; only the changed ones), 88 values:

| Plan | Weapon | Base | Mod |
|---|---|---|---|
| `pistol_belt` | Makarov, Star .45, 6P9 | 40/16/8/8 | 40/24/24/24 |
| | Desert Eagle | 40/16/8/8 | 40/24/21/21 |
| `light_assault_webbing` | MAC-10, Uzi | 150/60/30/30 | 150/120/90/90 |
| `shotgun_bandolier` | Ithaca, SPAS-12, USAS-12 | 60/24/12/12 | 60/32/32/30, 60/32/32/28, 60/32/32/26 |
| `assault_webbing` | MP5, AK-47, M16 | 150/60/30/30 | 150/120/90/90 |
| | FAL, G3KA4 | 150/60/30/30 | 150/120/90/60 |
| `marksmans_bandolier` | M1903 | 40/20/10/10 | 50/40/40/30 |
| | Dragunov | 40/20/10/10 | 40/30/30/30 |
| | AS50 | 40/20/10/10 | 40/20/20/15 |
| | dart rifle | 10/5/4/3 | 15/10/10/10 |
| `rocketeer_satchel` | RPG-7, Carl Gustaf, mortar | 3/2/1/1 | 5/4/4/4, 5/4/3/3, 9/7/6/6 |
| `grenadier_webbing` | M67, M79, IED | 3/2/1/1 | 3/2/2/2, 3/3/3/3, 3/2/2/2 |
| | crossbow (four new `bonus` lines) | - | 3/3/3/3 |
| `pyrotechnic_satchel` | flare gun | 6/6/3/3 | 6/6/2/2 |
| | LPO-50 | 500/200/100/100 | 500/200/200/200 |
| | Molotov | 3/2/1/1 | 4/3/3/2 |
| `gunner_pack` | PKM, M249 | 500/200/100/100 | 300/200/100/100 |

## Depends on

Nothing. The upgrades are sold from the start (`economy-shop-unlocked`). The MP5 fires SMG ammo in
this mod (`weapons-mp5-sd`) but its bonus stays in the assault webbing.

## Uncertain

- Whether a `maxammo` bonus keyed `object="mp5"` still raises the MP5's reserve once it shares the
  SMG pool is not traced. Not a line of the readme.
