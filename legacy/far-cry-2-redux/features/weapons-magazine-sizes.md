---
title: Real magazine sizes
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "**/weaponproperties/**#**/Ammo/iAmmoInClip"
exclude:
  - "**/weaponproperties/special/lpo50/multi.xml#**"
requires: []
verified: diff
---

# Real magazine sizes

The 7.62x51 battle rifles take 20-round magazines, the SPAS-12 holds 9 shells, the silenced shotgun
4 and the sawed-off fires both barrels before reloading. This is the "accurate magazine sizes" of
the mod's (unreachable) description.

## How

`CommonProperties/Ammo/iAmmoInClip`:

| Weapon properties | From | To |
|---|---|---|
| `Primary.FNFAL`, `.Persistent` | `30` | `20` |
| `Primary.G3KA4` | `30` | `20` |
| `Primary.SPAS12`, `.Persistent`, `.Multi` | `12` | `9` |
| `DLC1.SilencedShotgun`, `.Multi` | `6` | `4` |
| `DLC1.SawedOffShotgun`, `.Multi` | `1` | `2` |

The first three in `generated/entitylibrarypatchoverride.fcb`, the DLC shotguns in
`downloadcontent/dlc1/generated/entitylibrary.fcb`. Enemies and buddies carry the same
single-player FAL, G3 and SPAS-12 archetypes (`weapons-enemy-loadouts`), so theirs shrink too.

## Depends on

Nothing. Realism Plus cuts the silenced shotgun to 4 as well and the SPAS-12 to 9
([`weapons-shotguns`](../../realism-plus/features/weapons-shotguns.md)). The enemies' flamethrower
`LPO50.Multi` `200` -> `7` is on `weapons-enemy-flamethrower`.

## Uncertain

- Not a line of the readme. The FAL's and G3's `.Multi` copies keep 30 (multiplayer only).
