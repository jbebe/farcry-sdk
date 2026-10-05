---
title: Scripted briefcase message and collected-briefcase map icons
kind: component
claims: []
status: located
systems: [economy, ui]
match:
  - "_hash/05d31928.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_diamondbriefcasetracker_w?.*.xml"
  - "domino/system/pickuplistener.lua@*"
  - "worlds/*/generated/entitylibrary.fcb/domino/objectives/diamond.xml"
  - "worlds/*/generated/world*.mapsdata.fcb/diamondbriefcaseicon_*.xml"
  - "worlds/*/generated/world*.mapsdata.fcb/_layout.xml#layer[main]*/entity[diamondbriefcaseicon*]"
  - "languages/*/oasisstrings.fragment.xml#Diamonds/DiamondPickedSwoosh*"
  - "install/bin/dunia.dll@0x686{dbc,e07}"
exclude: []
requires: []
verified: re
---

# Scripted briefcase message and collected-briefcase map icons

When a diamond briefcase is picked up, a script shows "You found N out of 221 briefcases with
diamonds" and lights a marker on the map where the briefcase was. This stands in for the engine's
own message and markers, which reset whenever the mod swaps the player's map gadget (GPS range
upgrade, GPS/compass switch), so the mod patches the engine's message out of `Dunia.dll`. No line of
the published list names this piece; the gadget-swapping features need it.

## How

- **The script.** `_hash/05d31928.lua` (`domino\User\Diamonds\briefcasetracker.lua`, header "Track
  diamonds obtained via briefcases for updating the diamond counter"), run by
  `DominoOmniEntity_DiamondBriefCaseTracker_W1` / `_W2` in `world1/world2.omnis.fcb`. It creates one
  `Domino/System/PickupListener.lua` box per briefcase entity (224, both worlds, ids hard-coded) and
  on pickup: enables the map icon whose entity id is the briefcase id minus its last five digits
  (`CMapElementComponent_Enable`), adds 3, 4 or 5 to `DiamondCounter` (84, 92 and 44 briefcases; four more add 10), increments
  `DiamondBriefcasesCollected`, and pushes HUD objective `Diamonds`/`Diamond` with string
  `DiamondPickedSwoosh<count>`.
- **Listener support.** `domino/system/pickuplistener.lua` gains `EnableDiamond` (registers the
  `DominoCallbackPickupPicked` callback) and `Event_PickedUpDiamond` (returns the briefcase entity to
  the handler) - 2 hunks.
- **Map icons.** 224 new `DiamondBriefcaseIcon_<id>` entities in `world1.mapsdata.fcb` (117) and
  `world2.mapsdata.fcb` (107), `tplCreatureType` `Domino.Objectives.Diamond`, each at its briefcase's
  position, disabled until the script enables it. They are map markers, not briefcases and not
  locator targets. Each is placed in its world's `main` layer by one `_layout.xml` change
  (`layer[main][0]/entity[diamondbriefcaseicon_*]`).
- **Strings.** `Diamonds/DiamondPickedSwoosh1` ... `DiamondPickedSwoosh221` ("You found N out of 221
  briefcases with diamonds"), English text in all nine languages - 1,989 changes.

## Dunia.dll

`PushDiamondPicked` builds the engine's own "diamonds" HUD message (type `0x13`) on every pickup.
The `jmp +7` that leads to queuing it on the HUD manager becomes `jmp +0x2A`, past the queue call,
so the message never shows; the message object it built is leaked on each pickup (inferred). A
second patch NOPs a `char_traits::assign` call that only re-cleared a local string going out of
scope, with no effect (inferred).

| | Steam | GOG | Bytes |
|---|---|---|---|
| skip the queue | `0x10686DBC` | `0x106792AC` | `07 -> 2A` |
| string clear | `0x10686E07` | `0x106792F7` | `FF 15 ?? ?? ?? ?? -> 90 x6` |

Patterns (one match in each build; sites at +17 and +19; `??` masks addresses):
- `6A 13 8B C8 E8 ?? ?? ?? ?? 89 84 24 84 00 00 00 EB 07 89 9C 24 84 00 00 00 8D 84 24 84 00 00 00 50 8B CE E8`
- `C7 44 24 48 07 00 00 00 89 9C 24 8C 00 00 00 89 5C 24 44 FF 15 ?? ?? ?? ?? 83 C4 08 5F 5E 5D 5B 83 C4 6C C2 08 00`

## Depends on

- The `Dunia.dll` patch above; without it both messages show.
- `DiamondCounter` / `DiamondBriefcasesCollected` come from the shared
  `domino/user/master_gameglobals.globals.lua@L90` hunk; the counter itself is `economy-diamond-counter`.
- The +3/+4/+5 match `briefcase-diamonds`.

