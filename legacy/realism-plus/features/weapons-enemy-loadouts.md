---
title: Enemies carry the whole arsenal from the start
kind: component
bundle: gameplay
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{assault,shotgun,sniper,RocketLauncher,Mortar,warlord}]/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50/multi.xml#Entity/Components/**"
  - "generated/entitylibrarypatchoverride.fcb/{weapons,weaponproperties}/primary/as50/as50_merc.xml"
exclude:
  - "**/Pack[*]/Gadget@archetype"
requires: []
verified: diff
---

# Enemies carry the whole arsenal from the start

Mercenaries no longer move up the weapon ladder with the story: from the first hour a rifleman may
carry anything from a G3 to a PKM, shotgunners may carry a flamethrower, snipers may carry the AS50,
and sidearms include SMGs and the sawed-off.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks`. Each pack lists,
per progression level (`difficulty` 0-27), the weapons an archetype using it may draw, with
probabilities. The mod rewrites six packs so that nearly every weapon appears at every level (the
analysis can only pair the list entries by position, so the changes read as 1,882 field edits,
adds and removes):

| Pack | Base game | Mod (most levels; the last one or two keep a shorter list) |
|---|---|---|
| `assault` | G3 early, AK/FAL/M16 later, PKM/M249 late; sidearm Makarov, then Star .45, then Desert Eagle | AK-47 0.45, PKM 0.18, G3 0.11, FAL/M16/M249 0.07, MP5 0.04, golden AK-47 0.01; sidearm sawed-off 0.30, Star .45/Desert Eagle 0.25, Makarov 0.15, 6P9 0.05 |
| `shotgun` | Ithaca, then SPAS-12 | SPAS-12 about 0.5, USAS-12 0.2-0.25, Ithaca 0.15, silenced shotgun 0.05, flamethrower (`Special.LPO50.Multi`) 0.05; sidearms add MAC-10 and Uzi |
| `sniper` | M1903, then Dragunov | Dragunov 0.5, M1903 0.25, AS50 (`Primary.AS50.AS50_Merc`) 0.25; AS50 only at 27 |
| `RocketLauncher` | RPG-7, Carl Gustaf from level 13; level 27 names a broken `weapons.Special.RPG7_Merc` | RPG-7 0.5, Carl Gustaf 0.5 at every level |
| `RocketLauncher`, `Mortar`, `sniper`, `warlord` sidearms | Star .45, then MAC-10, then Uzi (`warlord`: Makarov, Star .45, Desert Eagle) | Uzi 0.60, MAC-10 0.20, Star .45 0.07, Makarov 0.06, Desert Eagle 0.05, 6P9 0.02 |

Two archetypes make the new weapons work for the AI:

- **Flamethrower.** The shotgun pack hands out the multiplayer flamethrower `weapons.Special.LPO50.Multi`,
  and its weapon properties `WeaponProperties.Special.LPO50.Multi` (in the base override library) are
  retuned for single-player use: `selCategory` `1` -> `3`; `archPickupArchetype`
  `pickups.Weapons.LPO50_new.Multi.Dropped` -> `pickups.Weapons.LPO50_new.Dropped` and
  `Collision/archFireStickyStream` `weapons.Special.FireStickyStream.Multi` -> `...FireStickyStream`
  (single-player pickup and fire); `sName` `lpo50` -> `lpo50new`; `vectorEffectiveRange` `30/30` ->
  `15/20`, `vectorEffectiveRangeIS` `30/30` -> `20/25`; `iClipsForSelfDestruct` `3` -> `6`;
  `iAmmoInClip` `200` -> `100`, `iMaxAmmo*` `300/300/300/300` -> `250/150/150/100`; `FlameMesh/fSize`
  `6` -> `30`, `fSpeed` `15` -> `30`; `Damage/Level` `10` -> `20` (17 values).
- **AS50.** Two new archetypes, `weapons.Primary.AS50.AS50_Merc` (a copy of the AS50 weapon entity)
  and `WeaponProperties.Primary.AS50.AS50_Merc` (a copy of the Dragunov's properties: `sName`
  `dragunov`, its fire rate, ammo, damage and spread, with the AS50 entity's model), whole units in
  `generated/entitylibrarypatchoverride.fcb`.

## Depends on

- Grenade changes in the same packs are `weapons-enemy-molotovs`; buddies are
  `weapons-buddy-loadouts`; packs for new enemy types are `weapons-new-enemy-packs` and the AI pages.
- Which enemy archetype uses which pack is the archetypes' `InventoryPack` value (AI pages).
- Scubrah's Patch varies patrol crews by swapping their classes instead
  ([`patrol-diverse-weapons`](../../scubrahs-patch/features/patrol-diverse-weapons.md)).

## Uncertain

- The `sniper` pack's special slot names `Primary.*` archetypes, as the base game does; the "special
  for the player is primary for enemies" convention is the guide's.
- The golden AK-47 in the assault pack (probability `0.01`) drops a golden AK-47 when its carrier
  dies (inference: the pickup comes from the weapon's own `archPickupArchetype`).
