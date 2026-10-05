---
title: Disabled leftovers of the old DLC and GPS purchase prompts
kind: noise
systems: [economy]
match:
  - "levels/*/generated/worldsectors/*.data.fcb/weaponpurchaseproximitytrigger_*.xml"
  - "levels/*/generated/worldsectors/*.data.fcb/diamondtrackerupgradepurchaseinteraction.*.xml"
  - "levels/*/generated/worldsectors/worldsector{3859,4766,2601,2697,1539,4738,4691,3485,2179,1971}.data.fcb/_layout.xml#layer[main]"
  - "_hash/{44152d6c,03b557bc}.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_bazaarwallpurchasemanager_w?.*.xml"
  - "languages/*/oasisstrings.fragment.xml#MessagesBoxHeaders/{PUR_TITLE,PUR_GPS_TITLE}"
  - "languages/*/oasisstrings.fragment.xml#MessagesBoxContents/{PUR_TEXT,PUR_TEXT_WEAPON*,PUR_GPS_TEXT}"
exclude: []
verified: diff
---

# Disabled leftovers of the old DLC and GPS purchase prompts

Before 3.6 the mod sold the DLC weapons, and before 3.1 the GPS range upgrade, through its own
"use" prompts and confirmation boxes instead of the bazaar menu. Those pieces still ship, but the
scripts now switch them off, so in play they do nothing (one visible prop aside).

## What is here

- **Purchase triggers.** 30 `WeaponPurchaseProximityTrigger_30`..`_59` entities, three per bazaar
  sector, and each sector's `_layout.xml` `layer[main]` entry that lists them (with the sector's
  existing `main` entities, so the entry adds nothing else).
- **Their scripts.** `_hash/44152d6c.lua` and `_hash/03b557bc.lua` ("Weapon Bazaar Purchase Manager
  (DLC - World1/World2)", `domino\User\Diamonds\bazaarpurchasemanager_w1/w2.lua`, run by
  `DominoOmniEntity_BazaarWallPurchaseManager_W1/_W2`). Their headers say they now only disable the
  old prompts: when the storage-room door is used they mark each trigger unusable, because the globals
  `DLC1_Weapon1/2/3Purchased` default to 1. The purchase handlers (35 diamonds each) are still in the
  files but unreachable. They also send `CProximityTriggerComponent_SetAsUsable` to the new DLC pickups,
  which carry no proximity trigger component (inference: no effect).
- **GPS prop.** `DiamondTrackerUpgradePurchaseInteraction` (entity `5439809847436664`) in the Pala
  bazaar sector `w1_c_3/worldsector2601`: a compass model with a trigger, made unusable by the GPS/
  compass switch script (`_hash/e3a9e9b7.lua`) whose old purchase handler is commented out. It stays
  visible as a prop.
- **Strings.** `PUR_TITLE`, `PUR_TEXT`, `PUR_TEXT_WEAPON1/2/3` (35-diamond DLC prompts) and
  `PUR_GPS_TITLE`, `PUR_GPS_TEXT` (GPS upgrade prompt), nine languages; only the unreachable handlers
  use them.

The live replacements are `dlc-weapons-in-shop` and `diamond-tracker-range-upgrade`.
