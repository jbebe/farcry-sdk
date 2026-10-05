---
title: More grenades from enemies
kind: component
bundle: balancing
claims:
  - "Increased the probability of enemies throwing grenades"
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/AdaptativeBehavior/Item[{0,1}]@level*"
exclude: []
requires: []
verified: diff
---

# More grenades from enemies

Enemies throw grenades far more often, from the start of the game.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/AdaptativeBehavior`:
the chance (percent) of each behaviour per adaptive level `level0`-`level27`.

- `Item[0]` `behavior="Grenade"`: `0`/`5`/`10`/`15`/`20` rising with level -> `45` at every level
- `Item[1]` `behavior="GrenadeAndBuilding"`: `10`-`50` rising -> `45` at every level (so the top
  seven levels drop from `50`)

## Uncertain

- The same block's `ChaseWithVehicle` and `VehicleChaseLevel2/3` (`fewer-vehicle-chases`) and
  `RescueVictim` `100` -> `50` (`Item[7]`) also change; `RescueVictim` matches no published line and
  is not claimed here.
