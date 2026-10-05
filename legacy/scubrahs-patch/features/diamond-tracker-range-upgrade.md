---
title: Diamond tracker range upgrade at the weapons bazaar
kind: component
bundle: gameplay
claims:
  - "Added a range upgrade for the diamond tracker for purchase at the weapons bazaar"
status: located
systems: [economy, ui]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Summary/Equipment/Item[gpsrange]"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[gps upgrade]"
  - "worlds/*/generated/world*.game.xml/missions/weaponbazaar/upgrade/gpsrange.xml"
  - "**/entitylibrary*.fcb/gadgets/equipped/{compasssingle_gpsupgrade,map_gpsupgrade,map_gpsupgrade_nomarker}.xml"
  - "languages/*/oasisstrings.fragment.xml#{Items,Bonus}/gpsrange"
  - "languages/*/oasisstrings.fragment.xml#WeaponBazaar/WEAPONBAZAAR_GPSRANGE_*"
exclude: []
requires: [economy-diamond-counter, economy-briefcase-tracker]
verified: diff
---

# Diamond tracker range upgrade at the weapons bazaar

A "GPS Range Upgrade" sold at the weapons bazaar for 50 diamonds. Once bought, the handheld map/GPS
is swapped for a copy whose diamond briefcase locator reaches farther: 100 m instead of 50 m
(inference from which library copy wins, see below).

## How

- **Shop entry.** `engine/gamemodes/gamemodesconfig.xml` gains `WeaponBazaar/Item[gps upgrade]`
  (`category="equipment" subcategory="gpsrange"`, `cost="50"`, `availability="1" needsUnlock="0"`,
  `bonus="none"`, `layer="Missions/WeaponBazaar/Upgrade/GPSRange"`, names
  `WEAPONBAZAAR_GPSRANGE_NAME`/`_DESCRIPTION`) and a `Summary/Equipment/Item[gpsrange]` catalogue entry.
  The engine only enables the layer; it grants nothing (`bonus="none"`).
- **Purchase detection.** New mission layer `Missions/WeaponBazaar/Upgrade/GPSRange` (off by default)
  in both `world*.game.xml`. The bazaar tracking script of `economy-diamond-counter`
  (`_hash/30713e26.lua`, `domino\User\Diamonds\bazaartracking.lua`) polls it every 3 s; when it flips
  and `GPSUpgradePurchased` is 0 it sets `GPSUpgradePurchased = 1`, takes 50 off its diamond count and
  equips `gadgets.Equipped.Map_GPSUpgrade` (or `Map_GPSUpgrade_NoMarker` when the user setting
  `ShowPlayerMapMarker` is 0) through `Domino/System/ManageInventory.lua` `AddGadget`.
- **The gadgets.** New archetypes in `worlds/world1` and `worlds/world2`:
  `gadgets.Equipped.Map_GPSUpgrade` = vanilla `Map` with `archCompassArchetype` ->
  `gadgets.Equipped.CompassSingle_GPSUpgrade`; `Map_GPSUpgrade_NoMarker` = the same without
  `archPlayerMarker`; `CompassSingle_GPSUpgrade` = vanilla `CompassSingle` with
  `CFCXCompassObjectives/DiamondLocator/fDistanceRange` `50 -> 70`. A copy of
  `CompassSingle_GPSUpgrade` in `generated/entitylibrarypatchoverride.fcb` sets it to `100`; the patch
  override is read over both world libraries, so 100 is presumably what plays.
- **Names.** `Items/gpsrange`, `Bonus/gpsrange` ("Range upgrade for the diamond briefcase tracker on
  your handheld GPS."), `WeaponBazaar/WEAPONBAZAAR_GPSRANGE_NAME` and `_DESCRIPTION`, nine languages.

## Depends on

- `economy-diamond-counter` - its script is what hands over the upgraded gadget.
- `economy-briefcase-tracker` - swapping the map gadget resets the engine's own briefcase count and
  markers, so the mod patches the engine's "briefcase found" message out of `Dunia.dll` and redraws it
  and the collected-briefcase map icons from script.
- `Globals.MASTER_GameGlobals.GPSUpgradePurchased`, `EquippedMapGadget` and `ShowPlayerMapMarker`
  are declared in the shared `domino/user/master_gameglobals.globals.lua@L90` hunk.
- The GPS/compass switch (`gps-compass-switch`, `_hash/e3a9e9b7.lua`) re-equips the upgraded GPS when
  switching back from the compass if `GPSUpgradePurchased` is 1.

## Uncertain

Before 3.1 the upgrade was bought at a GPS prop in the Pala bazaar; that prop and its strings
(`PUR_GPS_TITLE`, `PUR_GPS_TEXT`) remain, disabled - see `noise-economy-old-purchase-prompts`.
