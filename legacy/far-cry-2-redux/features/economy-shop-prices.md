---
title: Weapon shop prices
kind: component
bundle: gameplay
claims: []
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[*]@cost"
exclude: []
requires: []
verified: diff
---

# Weapon shop prices

Most guns cost a few diamonds more, the act-2 heavy weapons a few less, and the safehouse weapon
crates and camo suit are cheaper.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar/Item@cost` (diamonds), 24 items:

| Item | Base | Mod |
|---|---|---|
| `ithaca crate` | 4 | 5 |
| `g3ka4 crate`, `6p9 crate` | 6 | 8, 10 |
| `fnfal crate` | 10 | 14 |
| `ak47 crate`, `m1903 crate`, `mac10 crate`, `dart rifle crate`, `ied crate` | 10 | 12 |
| `de crate`, `pkm crate` | 10 | 20 |
| `m16 crate`, `mp5 crate`, `m79 crate`, `m249 crate` | 20 | 25 |
| `usas12 crate`, `mgl40 crate`, `as50 crate`, `carlgustaf crate` | 35 | 30 |
| `ithaca operation manual` | 1 | 2 |
| `camo suit` | 45 | 40 |
| `secondary`/`primary`/`special weapon crate` | 8/16/24 | 5/10/15 |

## Depends on

Nothing. The mod's mission-reward edits are on `missions-diamond-rewards`.

## Uncertain

- Not a line of the readme.
