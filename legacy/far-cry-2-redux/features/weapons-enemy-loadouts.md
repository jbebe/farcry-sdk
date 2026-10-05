---
title: Enemies carry a mixed arsenal from the start
kind: component
bundle: gameplay
claims: []
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{assault,shotgun,RocketLauncher,Mortar,sniper,warlord}]/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16/multi.xml#Entity/Components/CWeaponProperties/FireStrategyProperties/{fAngleYawBulletSpread,fAnglePitchBulletSpread}"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16/multi.xml#Entity/Components/CWeaponProperties/FireStrategyProperties/RangeMultipliers/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16/multi.xml#Entity/Components/CWeaponProperties/FireStrategyProperties/FirstPerson/fBulletSpread_MinimumSpreadPercentage"
exclude: []
requires: []
verified: diff
---

# Enemies carry a mixed arsenal from the start

Mercenaries no longer move up a weapon ladder with the story: every enemy class draws from one fixed
spread of weapons for the whole game. This is the "better weapon variety" of the mod's description.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks`. Each base pack
lists, per progression level (`difficulty` `0`-`27`), the weapons its carriers may draw; the mod
drops the levels and gives each pack one short probability list (the analysis pairs entries by
position, so the rewrite reads as 415 removes, adds and field edits):

| Pack | Base game (by level) | Mod (every level) |
|---|---|---|
| `assault` | G3 early, then AK, FAL, M16; PKM 7-23, M249 from 19; sidearm Makarov, Star .45, Desert Eagle | G3 0.35, AK-47 0.30, FAL 0.15, M16 (`Primary.M16.Multi`) 0.15, PKM 0.05; sidearm Makarov/Star .45 0.30, MAC-10 0.15, Desert Eagle/Uzi 0.10, M79 0.05 |
| `shotgun` | Ithaca, SPAS-12 from 5; sidearm as `assault` | Ithaca 0.45, SPAS-12 0.20, USAS-12 0.15, M249 0.15, flamethrower (`Special.LPO50.Multi`) 0.05; sidearm Makarov/Star .45 0.30, MAC-10 0.20, Desert Eagle/Uzi 0.10 |
| `RocketLauncher` | RPG-7, Carl Gustaf 13-25, level 27 a broken `weapons.Special.RPG7_Merc`; sidearm Star .45, MAC-10, Uzi | RPG-7 0.60, Carl Gustaf 0.40; sidearm Uzi/MAC-10 0.30, Desert Eagle 0.20, Makarov/Star .45 0.10 |
| `Mortar` | sidearm Star .45, MAC-10, Uzi | sidearm MAC-10/Uzi 0.50 |
| `sniper` | M1903 to 13, Dragunov from 14; sidearm Star .45, MAC-10, Uzi | M1903 0.40, Dragunov 0.40, AS50 (`Primary.AS50.AS50_Merc`) 0.20; sidearm Desert Eagle/6P9/MAC-10/Uzi 0.25 |
| `warlord` | Makarov, Star .45, Desert Eagle | Desert Eagle |

The flare gun special and the grenades stay as they were.

The assault pack's M16 is the multiplayer archetype `weapons.Primary.M16.Multi`, so its weapon
properties apply to enemies here. The mod's library differs from the base in four of them (the
re-export's multiplayer values, see `noise-weapons-multiplayer`): `fAngleYawBulletSpread` `0.1` ->
`2`, `fAnglePitchBulletSpread` `2.5` -> `2`, the near/far `RangeMultipliers` boundary `7` -> `3` (four
values), `FirstPerson/fBulletSpread_MinimumSpreadPercentage` `0.1` -> `0.7`; they are claimed here
because enemies now fire it. Its full-auto change is `weapons-m16-full-auto`.

## Depends on

- `weapons-enemy-flamethrower` makes the flamethrower usable by the AI; without it the shotgunners
  carry the multiplayer flamethrower as is.
- Buddies are `weapons-buddy-loadouts`. Realism Plus flattens the same packs with different weights
  ([`weapons-enemy-loadouts`](../../realism-plus/features/weapons-enemy-loadouts.md)).

## Uncertain

- `weapons.Primary.AS50.AS50_Merc` exists neither in the base libraries nor in this mod's (Realism
  Plus adds it as a new archetype); a fifth of the snipers may get no weapon or a fallback. Not
  checked in game.
- Not a line of the readme.
