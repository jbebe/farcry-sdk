---
title: Phone on the gadget cycle
kind: component
bundle: controls
claims: []
status: located
systems: [player, input]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/GadgetCategories/**"
exclude: []
requires: []
verified: diff
---

# Phone on the gadget cycle

The phone becomes an equippable gadget like the map, reachable with the next-gadget key.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/GadgetCategories`:
`Item[3]` (`category="phone"`) `slot` `0` -> `1`, the slot of the map. The base file's comment says
non-equippable gadgets must use slot 0, which the engine relies on to skip them when selecting the
next or previous gadget.

## Depends on

Nothing. The next-gadget key is Tab in this mod (`input-map-key-tab`).

## Uncertain

- What equipping the phone does in hand (look at it, answer it) is not checked in game. Not a line
  of the readme.
