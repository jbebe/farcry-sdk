---
title: Shotgunners carry Molotovs
kind: component
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#**/InventoryPacks/Pack[shotgun]/Gadget@archetype"
exclude: []
requires: []
verified: diff
---

# Shotgunners carry Molotovs

Enemies of the shotgun class throw Molotov cocktails instead of M67 grenades. The published list does
not mention this; it adds to the fire the mod makes spread further (`fire-spread`).

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks/Pack[shotgun]`: the
pack's gadget `archetype` goes from `gadgets.Grenades.M67` to `gadgets.Grenades.Molotov`.