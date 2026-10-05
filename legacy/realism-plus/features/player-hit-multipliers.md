---
title: Player shots hit harder, headshots twice as hard
kind: component
bundle: gameplay
status: located
systems: [player, weapons, ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/HitLocations/**"
exclude: []
requires: []
verified: diff
---

# Player shots hit harder, headshots twice as hard

Everything the player shoots takes more damage per hit: double on the head, two and a half times on
the body and arms.

## How

`engine/gamemodes/gamemodesconfig.xml`, the single-player `DefaultCountersService/HitLocations`
damage multipliers for the player's hits
([player character](../../../docs/docs/modding/guide/player-character.md#damage-dealt-to-enemies)):

| Location | Base | Mod |
|---|---|---|
| `Head` | `6.0` | `12.0` |
| `Torso` | `1.0` | `2.5` |
| `Arms` | `1.0` | `2.5` |
| `Legs` | `0.5` | `0.75` |
| `Hands` | `0.5` | `1.0` |
| `Feet` | `0.5` | `0.75` |

## Uncertain

- The paraphrased v2.1 list names an increased headshot multiplier and an optional double damage;
  this build has one setting, and the multiplayer `HitLocations` blocks are untouched.
