---
title: DLC weapons sold at the weapons bazaar
kind: component
bundle: balancing
claims:
  - "DLC weapons can now be purchased from the weapons bazaar"
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Summary/Weapons/Item[{silencedshotgun,sawedoffshotgun,crossbow}]"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[{silencedshotgun,sawedoffshotgun,crossbow} crate]"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/dlc/*.xml"
  - "levels/*/generated/worldsectors/*.data.fcb/_layout.xml#layer[missions*weaponbazaar*dlc*]{,/**}"
  - "levels/*/generated/worldsectors/*.data.fcb/missions_weaponbazaar_{crossbow,sawedoffshotgun,silencedshotgun}_new.weaponstorage.*.xml"
  - "domino/system/dlc1weaponsspawn.lua@*"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/dlc1weapons/dlc1/pickup_*.xml#**/fRespawnTime"
  - "languages/*/oasisstrings.fragment.xml#{Tutorial,WeaponBazaar}/WEAPONBAZAAR_{QUIETSHOTGUN,SAWEDOFF,CROSSBOW}_CRATE_NAME"
  - "languages/*/oasisstrings.fragment.xml#{Challenges,Items}/{silencedshotgun,sawedoffshotgun,crossbow}"
exclude: []
requires: []
verified: diff
---

# DLC weapons sold at the weapons bazaar

The three Fortune's Pack weapons - silenced ("Suppressed") shotgun, sawed-off shotgun and explosive
crossbow - stop lying free on a crate in every bazaar and become bazaar purchases like any other gun,
available from the start with no unlock.

## How

- **Free copies removed.** `domino/system/dlc1weaponsspawn.lua` comments out the three
  `SpawnEntityFromArchetype` weapon calls at each of the ten bazaar crate sites (10 hunks). The DLC
  crate itself still spawns, now empty.
- **Shop entries.** `engine/gamemodes/gamemodesconfig.xml` gains `WeaponBazaar/Item` entries
  `silencedshotgun crate` (primary, cost `40`), `sawedoffshotgun crate` (secondary, `25`) and
  `crossbow crate` (special, `40`), all `availability="0" needsUnlock="0" unlockUpgrade="0"`, each with
  `layer="Missions/WeaponBazaar/DLC/<Weapon>"`, plus matching `WeaponBazaar/Summary/Weapons/Item`
  catalogue entries (each listing the crate and an ammo bag: shotgun bandolier, or rocketeer satchel
  for the crossbow).
- **What a purchase turns on.** Three new mission layers per world in `world1/world2.game.xml`
  (`Missions/WeaponBazaar/DLC/SilencedShotgun`, `SawedOffShotgun`, `Crossbow`, off by default). The
  bazaar enables an item's `layer` when it is bought.
- **The guns in the storage room.** In each of the ten bazaar sectors (`w1_b_2`, `w1_b_3`, `w1_c_3`,
  `w1_c_4`, `w1_d_2`, `w2_b_2`, `w2_b_4`, `w2_c_3`, `w2_d_2`, `w2_d_4`) one weapon pickup per gun
  (`missions\weaponbazaar\<Gun>_new.WeaponStorage`, `tplCreatureType` `DLC1Weapons.DLC1.Pickup_<Gun>`,
  entity-level `fRespawnTime` `0`) and a `_layout.xml` layer that puts it in
  `missions\weaponbazaar\dlc\<gun>` - 30 entities and 30 layer entries.
- **Archetypes.** `fRespawnTime` `0.1 -> 0` on `DLC1Weapons.DLC1.Pickup_Crossbow`,
  `Pickup_SawedOffShotgun` and `Pickup_SilencedShotgun` in `downloadcontent/dlc1` - the same change the
  mod makes to every vanilla `WeaponStorage`/`StorageRoom` pickup for its armory cooldown.
- **Names.** `WEAPONBAZAAR_QUIETSHOTGUN_CRATE_NAME`, `..._SAWEDOFF_...`, `..._CROSSBOW_CRATE_NAME` in
  both the `Tutorial` and `WeaponBazaar` sections, and `Challenges`/`Items` entries
  `silencedshotgun`, `sawedoffshotgun`, `crossbow`, all nine languages.

## Depends on

- The pickups carry `fRespawnTime 0`, so a bought DLC gun is a single pickup until the armory
  cooldown script (`armory-respawn-cooldown`, `_hash/07d356ff.lua` / `_hash/40732c2f.lua`) puts it
  back; it tracks these entity ids. Without it, inference: the gun does not come back.
- `dlc-ammo-upgrades` makes the ammo bag each catalogue entry lists actually raise that gun's ammo.

## Uncertain

The mod still places an older purchase system for the same three guns (proximity triggers and
`_hash/44152d6c.lua` / `_hash/03b557bc.lua`) that the scripts now disable; that is
`noise-economy-old-purchase-prompts`, not needed here.
