---
title: Harder body shots and headshots
kind: component
bundle: gameplay
claims: []
status: located
systems: [player, weapons, ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/HitLocations/**"
exclude: []
requires: []
verified: diff
---

# Harder body shots and headshots

The player's hits to the head and torso do more damage: two and a half times on the body, a quarter
more on the head.

## How

`engine/gamemodes/gamemodesconfig.xml`, single-player `DefaultCountersService/HitLocations`
([player character guide](../../../docs/docs/modding/guide/player-character.md#damage-dealt-to-enemies)):
`Head@multiplier` `6.0` -> `7.5`, `Torso@multiplier` `1.0` -> `2.5`. Arms, legs, hands and feet keep
theirs.

## Depends on

Nothing. Realism Plus raises the same block further (head `12`, torso and arms `2.5`,
[`player-hit-multipliers`](../../realism-plus/features/player-hit-multipliers.md)).

## Uncertain

- Part of the description's "better ballistics" presumably; not a line of the readme.
