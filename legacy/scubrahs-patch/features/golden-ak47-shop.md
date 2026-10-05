---
title: Golden AK-47 at the weapons bazaar
kind: component
bundle: gameplay
claims:
  - "Added the golden AK-47 for purchase at the weapons bazaar (unlocked after 10 buddy/bar missions)"
status: located
systems: [economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Summary/Weapons/Item[goldak47]"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[goldak47 crate]"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[assault_webbing]/bonus[+*]"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/primary/goldak47.xml"
  - "levels/*/generated/worldsectors/*.data.fcb/_layout.xml#layer[missions*weaponbazaar*primary*goldak47]"
  - "levels/*/generated/worldsectors/*.data.fcb/missions_weaponbazaar_goldak47_new.weaponstorage.*.xml"
  - "levels/*/generated/worldsectors/*.data.fcb/dlc1.weaponcrate_customweapons.*.xml"
  - "**/entitylibrary*.fcb/pickups/weapons/ak47_dropped/ak47_gold.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/ak47_new/ak47_gold.xml"
  - "**/entitylibrary*.fcb/weaponproperties/primary/ak47/ak47_gold.xml#**/archPickupArchetype"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/ak47/ak47_gold.xml"
  - "languages/*/oasisstrings.fragment.xml#{Tutorial,WeaponBazaar}/WEAPONBAZAAR_GOLDAK47_CRATE_NAME"
  - "languages/*/oasisstrings.fragment.xml#{Challenges,Items}/goldak47"
  - "languages/*/oasisstrings.fragment.xml#Mission/GoldAKAvailable"
exclude: []
requires: [missions-mission-completed]
verified: diff
---

# Golden AK-47 at the weapons bazaar

The unused golden AK-47 becomes a bazaar weapon costing 150 diamonds. It is locked until the player
has completed ten buddy side quests, at which point a HUD message says it is available.

## How

- **Shop entry.** `engine/gamemodes/gamemodesconfig.xml` gains `WeaponBazaar/Item[goldak47 crate]`
  (primary, `cost="150"`, `availability="1" needsUnlock="1"`,
  `layer="Missions/WeaponBazaar/Primary/GoldAK47"`, name `WEAPONBAZAAR_GOLDAK47_CRATE_NAME`, the AK-47's
  description and icon), a `Summary/Weapons/Item[goldak47]` catalogue entry (crate, `ak47_challenge`,
  assault webbing), and four `BonusService/Plan[assault_webbing]` `maxammo` bonuses for
  `object="goldak47"` (`150/60/30/30`, the AK-47's own values).
- **The unlock.** The `UnlockItem("goldak47 crate")` call lives in the shared
  `domino/system/missioncompleted.lua@L32` hunk: it counts completed buddy side quests
  (`Globals.MASTER_GameGlobals.BSQMissionsCompleted`) and at 10 unlocks the crate and pushes the
  `Mission/GoldAKAvailable` HUD message. That hunk is claimed by `missions-mission-completed`.
- **What a purchase turns on.** Mission layer `Missions/WeaponBazaar/Primary/GoldAK47` in both
  `world*.game.xml`, and in each of the ten bazaar sectors a `_layout.xml` layer
  `missions\weaponbazaar\primary\goldak47` holding two new entities: the gun
  (`missions\weaponbazaar\GoldAK47_new.WeaponStorage`, `archWeapon` `weapons.Primary.AK47.AK47_gold`,
  ammo `30-90`, entity-level `fRespawnTime 0`) and a crate model to rest it on
  (`DLC1.WeaponCrate_CustomWeapons`, `graphics\objects\industrial\boxes\boxweapons_dlc.xbg`).
- **Archetypes.** A new pickup `pickups.Weapons.AK47_dropped.AK47_Gold` (in `world1`, `world2`,
  `tmpla` and the patch override) and `WeaponProperties.Primary.AK47.AK47_Gold` `archPickupArchetype`
  `pickups.Weapons.AK47_new.AK47_Gold -> pickups.Weapons.AK47_dropped.AK47_Gold` (world1, world2,
  tmpla, and a whole copy in `generated/entitylibrarypatchoverride.fcb`), so a dropped golden AK
  becomes a normal pickup. The vanilla pickup `pickups.Weapons.AK47_new.AK47_Gold` gets
  `bEnable`/`bPickable` `True -> False` in the patch override; inference: so the gun cannot be had
  anywhere else for free.
- **Names.** `WEAPONBAZAAR_GOLDAK47_CRATE_NAME` (`Tutorial` and `WeaponBazaar` sections),
  `Challenges/goldak47`, `Items/goldak47` ("Golden AK-47") and `Mission/GoldAKAvailable`, all nine
  languages.

## Depends on

- `domino/system/missioncompleted.lua@L32` for the unlock - one hunk that also carries the buddy
  side-quest reward, contextual "mission completed" messages, the airport fix and more; it is
  claimed by `missions-mission-completed`, which this page requires.
- `Globals.MASTER_GameGlobals.BSQMissionsCompleted`, declared in the shared
  `domino/user/master_gameglobals.globals.lua@L90` hunk.
- The gun pickup has `fRespawnTime 0`; the armory cooldown script (`armory-respawn-cooldown`) is
  what puts it back after it is taken.

## Uncertain

The patch-override copy of `WeaponProperties.Primary.AK47.AK47_Gold` is a whole unit that also
carries `fIronsightFOV 0.95 -> 1` (the `ads-fov` change); the same value is also set field by field
in the world1/world2 libraries. `domino/user/sidemissions/convoymissions.unlockweapons.lua@L369` holds
a commented-out earlier unlock call (noise).
