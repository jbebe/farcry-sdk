---
title: Almost everything sold from the start
kind: component
bundle: gameplay
claims: []
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[*]@availability"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[*]@needsUnlock"
exclude: []
requires: []
verified: diff
---

# Almost everything sold from the start

The arms dealer offers nearly his whole stock from the first visit: most weapons, every manual, ammo
upgrade, vehicle manual, the camo suit and the weapon crates. Convoy unlocks are still needed where
the base game asks for them, except for the flare gun.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar/Item@availability` -> `0` (the value the
base game gives the starter Makarov, Ithaca and G3KA4) on 89 items:

- **Weapons.** The crates of the SPAS-12, AK-47, MP5, Dragunov, M1903, Star .45, MAC-10, M79, flare
  gun, 6P9, dart rifle, RPG-7, PKM, LPO-50 and IED (from `1`), and of the MGL140, Carl Gustaf and
  mortar (from `2`). The USAS-12, FAL, M16, AS50, Desert Eagle, Uzi and M249 still wait for act 2
  (`2`).
- **Manuals.** All 48 operation and repair manuals for the weapons not already at `0`, and the eight
  vehicle repair manuals.
- **Equipment.** The nine ammo upgrades (pistol belt to gunner pack), the camo suit, the medium and
  large first aid manuals, and the secondary, primary and special weapon crates.

`Item[flare crate]@needsUnlock` `1` -> `0`: the flare gun no longer needs a convoy.

## Depends on

Nothing. Prices are `economy-shop-prices`. Realism Plus brings only the ammo upgrades and first aid
forward ([`economy-early-upgrades`](../../realism-plus/features/economy-early-upgrades.md)).

## Uncertain

- `availability` read as the act from which an item is offered (`0` from the start); not traced.
  Not a line of the readme.
