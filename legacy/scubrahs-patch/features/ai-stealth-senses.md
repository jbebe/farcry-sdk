---
title: Stealthier AI senses
kind: component
bundle: balancing
claims:
  - "Tweaked enemy AI sensory system to make stealth slightly more viable"
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/FCXAIBehaviorService/{IntimateSocialDistance,MediumSocialDistance}@value"
exclude: []
requires: [player-enemy-override-copies]
verified: diff
---

# Stealthier AI senses

Alert soldiers see a narrower field around them and notice a player in a vehicle less, so slipping
past or away is a bit easier.

## How

- In the 21 soldier archetypes the mod copies into `generated/entitylibrarypatchoverride.fcb`,
  `CFCXAIComponent/AIObject/CPawnAgent/SensorySystem/FOVParameters/FOVMultipliers`:
  - `fPlayerInVehicleMultiplier` `2` -> `1.7` (all 21)
  - `fPreCombatMultiplier` `0.75` -> `0.6`, `fCombatMultiplier` `1` -> `0.9`,
    `fPostCombatMultiplier` `1.25` -> `0.8` (the 12 riflemen, machine gunners and shotgunners)
- `engine/gamemodes/gamemodesconfig.xml`, `FCXAIBehaviorService`: `IntimateSocialDistance` `1.5` ->
  `2.5` and `MediumSocialDistance` `8` -> `10`.

## Depends on

The sensory multipliers exist only in the copies owned by `player-enemy-override-copies`.

## Uncertain

- The two social distances are placed here by inference: they sit among the detection-duration
  settings (`IntimateRangeDetectionDuration*`, `MediumRangeDetectionDuration*`) and plausibly bound
  those ranges. Widening them could as well make detection faster at that range; not traced.
