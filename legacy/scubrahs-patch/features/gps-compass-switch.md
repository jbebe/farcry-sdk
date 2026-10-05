---
title: Swap the GPS for a real compass
kind: component
bundle: gameplay
claims:
  - "Added the ability to switch between the GPS and compass gadgets (Pala - Port Selao)"
status: located
systems: [player, ui]
match:
  - "worlds/*/generated/entitylibrary.fcb/gadgets/equipped/{compassmulti,map_realcompass,map_realcompass_nomarker}.xml"
  - "graphics/objects/mapcompass/compassmulti_ref.skeleton"
  - "_hash/e3a9e9b7.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_gpsupgradepurchasemanager.*"
  - "levels/*/generated/worldsectors/worldsector{3080,3160}.data.fcb/playergadgetswap_interaction_*"
  # the sectors' layouts: today one whole layer[main] each; split per entity after a re-analysis
  - "levels/*/generated/worldsectors/worldsector{3080,3160}.data.fcb/_layout.xml#layer[main]"
  - "levels/*/generated/worldsectors/worldsector{3080,3160}.data.fcb/_layout.xml#layer[main]/entity[{playergadgetswap_interaction_*,furniture.urbantableround01_bk_19}]"
  - "levels/*/generated/worldsectors/worldsector{3080,3160}.data.fcb/_layout.xml#delete[{DetailObjects.GarbageNewpaper_BK_56,StaticObject_60399}.*]"
  - "levels/w1_c_3/generated/worldsectors/worldsector3080.data.fcb/furniture.urbantableround01_bk_19.**"
exclude: []
requires: []
verified: diff
---

# Swap the GPS for a real compass

In Pala and in Port Selao a compass and a GPS lie side by side; using one swaps the map's gadget, so
the player can carry the needle compass of multiplayer instead of the GPS, and back.

## How

- **Gadgets** (new in `worlds/world1` and `worlds/world2`):
  - `gadgets.Equipped.Map_RealCompass` - a copy of `gadgets.Equipped.Map` whose
    `CGadget/UseStrategy/archCompassArchetype` is `gadgets.Equipped.CompassMulti` instead of
    `CompassSingle`, and whose `MarkerVisibility/KeyLocation` also shows `CellTower` and
    `BusStation`.
  - `Map_RealCompass_NoMarker` - the same without the player marker (for the map-marker setting).
  - `gadgets.Equipped.CompassMulti` - the multiplayer compass, which the base game keeps only in
    `worlds/tmpla`, brought into the campaign worlds with `CSimpleAnimationComponent/sPartName`
    `compassmulti` -> `compass` (and `bIntelHackGliderOn` dropped).
  - `graphics/objects/mapcompass/compassmulti_ref.skeleton` renames its root bone `CompassMulti` ->
    `Compass` (873 -> 868 bytes), so the single-player hand animations drive it.
- **Props.** Two new entities, `PLAYERGADGETSWAP_Interaction_ToCompass` (`5439809847436111`, the
  `compassmulti.xbg` model) and `PLAYERGADGETSWAP_Interaction_ToGPS` (`5439809847436222`), in
  `worldsector3080` (Pala, on the round table `Furniture.UrbanTableRound01_BK_19`) and
  `worldsector3160` (Port Selao). Each sector's `_layout.xml` adds them to layer `main` and deletes
  the object that stood in the spot (`DetailObjects.GarbageNewpaper_BK_56`, `StaticObject_60399`).
  The Pala table comes back from the editor with its compound-physics states written out in full
  (144 changes; inferred to be a re-export side effect, the table's own values).
  In this analysis each layout's `layer[main]` is one change that also lists the sector's other
  entities the mod stages; those belong to whichever pages own them once the layout is split per
  entity.
- **Script.** `_hash/e3a9e9b7.lua` (`domino\User\Diamonds\diamondtrackerupgrade.lua`, run by the
  omni entity `DominoOmniEntity_GPSUpgradePurchaseManager` in both worlds) puts a proximity trigger
  on each prop. `SwapToCompass` gives `Map_RealCompass` (or `_NoMarker`); `SwapToGPS` gives
  `Map_GPSUpgrade`/`Map` (or their `_NoMarker` twins) depending on `GPSUpgradePurchased` and
  `ShowPlayerMapMarker`, through `Domino/System/ManageInventory.lua`'s `AddGadget`, and records the
  choice in `EquippedMapGadget` (`1`-`6`).

## Depends on

- `diamond-tracker-range-upgrade` owns `Map_GPSUpgrade` and its twins, and `user-configurable` owns
  `Map_NoMarker` and the setting `ShowPlayerMapMarker`; `graphics-environment-manager` re-picks the
  gadget when that setting changes. The globals `EquippedMapGadget`, `GPSUpgradePurchased` and
  `ShowPlayerMapMarker` are declared in `missions-game-globals`.
- The script's name and header are the old diamond-tracker upgrade purchase (update 2.9 turned it
  into the swap); it still holds that dead code and a store-door listener that disables the old
  purchase prop `5439809847436664` (`noise-economy-old-purchase-prompts`).
