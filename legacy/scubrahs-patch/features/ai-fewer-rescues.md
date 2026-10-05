---
title: Enemies drag wounded comrades less often
kind: component
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#**/AdaptativeBehavior/Item[7]@*"
exclude: []
requires: []
verified: diff
---

# Enemies drag wounded comrades less often

Enemies are half as likely to run out and drag a downed comrade to cover. The published list does
not mention this.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/AdaptativeBehavior`:
the item with `behavior="RescueVictim"` (index 7) goes from `100` to `50` at every adaptive level
(`level0`-`level27`). The other items of that list are `enemy-grenade-probability` (Grenade,
GrenadeAndBuilding) and `fewer-vehicle-chases` (ChaseWithVehicle, VehicleChaseLevel2/3).

## Uncertain

- That the values are probabilities in percent is read from the other items' use, not traced.
