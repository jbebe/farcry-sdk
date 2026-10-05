---
title: Ammo and first-aid upgrades sold from act 1
kind: component
bundle: gameplay
status: located
systems: [economy]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[{assault webbing,marksmans bandolier,rocketeer satchel,pyrotechnic satchel,gunner pack,large first aid manual}]@availability"
exclude: []
requires: []
verified: diff
---

# Ammo and first-aid upgrades sold from act 1

Every ammo upgrade and the large first aid manual can be bought in act 1 instead of waiting for act 2.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar/Item@availability` `2` -> `1` on `assault webbing`,
`marksmans bandolier`, `rocketeer satchel`, `pyrotechnic satchel`, `gunner pack` and
`large first aid manual`. Prices are unchanged. The other bandoliers were already act 1.

## Depends on

Nothing. The vehicle repair manuals brought forward are `economy-vehicle-manuals`.

## Uncertain

- `availability` read as the act from which an item is offered (0 from the start); not traced.
