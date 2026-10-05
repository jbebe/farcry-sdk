---
title: Weapons bazaar rearranged, with its unlocks rewired
kind: component
bundle: weapons
status: located
systems: [economy, weapons, missions]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[* {crate,operation manual,repair manual}]@*"
  - "domino/user/sidemissions/convoymissions.unlockweapons.lua@*"
exclude:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[{jeep wrangler,buggy,datsun,jeep liberty,rover,fishingboat,big truck,swampboat} repair manual]@*"
requires: []
verified: diff
---

# Weapons bazaar rearranged, with its unlocks rewired

The arms dealer lists sidearms first, then primaries, then specials. Behind that, a few weapons change
when they become available: the flare gun and the IED are sold from the start, the MP5 is sold as a
secondary from act 2 without an unlock, and the Uzi becomes a convoy unlock in its place.

## How

**The listing.** `engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar`: the 28 weapon crates and 56
operation and repair manuals keep their `name` keys in the base game's order, but the rest of each
line (what it sells, its `layer`, `nameOasis`, `descriptionOasis`, `icon`, `subcategory`, `cost`,
`availability`, `needsUnlock`, the manuals' `bonus` and `dependencyItem`) is moved to another key. The
key `ithaca crate` now sells the Makarov, `spas12 crate` the 6P9, `usas12 crate` the Star .45,
`fnfal crate` the Desert Eagle, `g3ka4 crate` the MAC-10, `ak47 crate` the Uzi, `m16 crate` the MP5,
`mp5 crate` the flare gun, `dragunov crate` the M79, `mgl40 crate` the IED; then `as50 crate` the G3,
`m1903 crate` the AK-47, `makarov crate` the FAL, `star45 crate` the M16, `de crate` the Ithaca,
`mac10 crate` the SPAS-12, `uzi crate` the USAS-12, `m79 crate` the MGL140, `flare crate` the M1903,
`6p9 crate` the Dragunov, `dart rifle crate` the AS50; `rpg7 crate` the dart rifle, `carlgustaf crate`
the PKM, `pkm crate` the M249, `m249 crate` the LPO-50, `lpo50 crate` the RPG-7, `mortar crate` the
Carl Gustaf and `ied crate` the mortar. Each manual follows its weapon. That is 520 attribute changes.

**What actually changes per weapon** (everything else keeps its price, act and unlock):

| Weapon | Base game | Mod |
|---|---|---|
| Flare gun | `availability` 1, `needsUnlock` 1 (convoy) | `0`, `0`: sold from the start; listed `secondary` |
| IED | `availability` 1, `subcategory` `explosives` | `0`, `secondary` |
| MP5 | `availability` 1, `needsUnlock` 1, `primary` | `2`, `0`: act 2 without an unlock; listed `secondary` |
| Uzi | `availability` 2, `needsUnlock` 0 | `1`, `1`: act 1 after a convoy |
| their manuals | Uzi `2`, MP5 `1`, flare gun `1` | Uzi `1`, MP5 `2`, flare gun `0` |

**The unlocks.** Convoy missions unlock bazaar items by key, through
`domino/user/sidemissions/convoymissions.unlockweapons.lua` (19 one-line hunks, `self[N].Item = "<key>"`).
The mod rewrites each key so that every convoy still unlocks the same weapon under its new key
(for example the mortar's convoy `"mortar crate"` -> `"ied crate"`), except two: the convoy that
unlocked the MP5 (`self[21]`) now unlocks the Dragunov, and the one that unlocked the Dragunov
(`self[18]`) now unlocks the Uzi.

## Depends on

- `weapons-flare-gun-gadget` puts the flare gun on the gadget slot; `weapons-mp5-smg` gives the MP5
  the SMG ammo. Prices of other items and the vehicle manuals are `economy-early-upgrades` and
  `economy-vehicle-manuals`.
- The key moves only make sense together with the script hunks: picking one without the other
  unlocks the wrong weapons.

## Uncertain

- Unlocks are stored by key, so a save started without the mod maps its unlocked keys onto other
  weapons (inference); start a new game.
- `WeaponBazaar/Summary/Weapons`, unchanged, still pairs each weapon with its base-game key (`ithaca`
  -> `ithaca crate`, which now sells the Makarov); what the shop's weapon pages show is not checked.
- That the shop lists items in file order, which would explain the move, is inferred.
