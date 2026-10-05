---
title: Script-side diamond counter
kind: shared
claims: []
status: located
systems: [economy]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[*]@layer"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/**"
  - "_hash/30713e26.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_bazaardiamondtracker_w?.*.xml"
  - "domino/system/givemissionreward.lua@L33"
exclude:
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/dlc/**"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/primary/goldak47.xml"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/upgrade/gpsrange.xml"
requires: []
verified: diff
---

# Script-side diamond counter

Domino scripts can give diamonds (`AddDiamonds`) but have no way to read the wallet, so the mod keeps
its own running count, `Globals.MASTER_GameGlobals.DiamondCounter`, and updates it from every place
diamonds come and go. Its live consumer is the paid fast travel (`fasttravel.fasttravel.lua` checks
`DiamondCounter >= FastTravelCost`); the same script also delivers the GPS range upgrade. No line of
the published list names this piece; it is shared plumbing.

## How

- **Every bazaar purchase is made visible.** The bazaar enables an item's `layer` mission when it is
  bought, but in vanilla only weapon crates have one. The mod adds `layer="Missions/WeaponBazaar/..."`
  to every other `WeaponBazaar/Item` in `engine/gamemodes/gamemodesconfig.xml` - operation and repair
  manuals, vehicle repair manuals, ammo bags, camo suit, first aid manuals, the three magic crates -
  and declares those layers, empty and off by default, in `world1.game.xml` and `world2.game.xml`
  (`missions/weaponbazaar/{primary,secondary,special,vehicle,ammo,upgrade,crate}/*`).
- **The tracker.** `_hash/30713e26.lua` (`domino\User\Diamonds\bazaartracking.lua`, header "Track
  purchases from the weapons bazaar"), run by `DominoOmniEntity_BazaarDiamondTracker_W1` /
  `_W2` in `world1/world2.omnis.fcb`. Every 3 s it reads `IsEnabled()` on 110 bazaar layers -
  the vanilla weapon crate layers, the mod's new ones, the DLC and golden AK layers - and when one
  flips it subtracts that item's price from `DiamondCounter`. The prices are hard-coded and equal the
  mod's new costs (`shop-prices`), e.g. AK-47 40, golden AK 150, GPS upgrade 50. The GPS upgrade layer
  additionally sets `GPSUpgradePurchased` and equips the upgraded map (`diamond-tracker-range-upgrade`).
- **Mission rewards.** `domino/system/givemissionreward.lua@L33` adds each mission's reward to
  `DiamondCounter` by mission id: `ASSW1`/`ASSW2` 20, story missions `A1SM02` 25, `A2SM06` 25,
  `A2SM08` 40, `A3SM11` 40, `A3SM12` 30, `A3SM13` 30, library missions `A1LM01`-`A1LM06` 20,
  `A2LM07`-`A2LM12` 35 - the mod's raised rewards.
- Briefcases (`economy-briefcase-tracker`) and buddy side quests (the shared
  `domino/system/missioncompleted.lua@L32` hunk) add to it too.

## Depends on

- `DiamondCounter` is declared (starting at 0) in the shared `domino/user/master_gameglobals.globals.lua@L90`
  hunk.
- The hard-coded prices and rewards assume `shop-prices`, `economy-mission-rewards`,
  `assassinations-20-diamonds` and `briefcase-diamonds`; with vanilla values the count drifts.

## Uncertain

The counter starts at 0 on a new game while the real wallet does not track it after a save made
without the mod, so on an existing save the two disagree (inference; the mod asks for a new game).
