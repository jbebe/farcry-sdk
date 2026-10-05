---
title: More grenades dropped on the hard settings, less ammo on Casual
kind: component
bundle: gameplay
status: located
systems: [ai, economy, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/{ChanceToDropGrenade,ClipMultiplierForPickup}@*"
exclude: []
requires: []
verified: diff
---

# More grenades dropped on the hard settings, less ammo on Casual

Killed enemies drop a grenade more often on Hardcore and Infamous, and on Casual they drop as much
ammunition as on Normal instead of twice as much.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement`, per difficulty
(Boggalog's guide: [enemies](../../../docs/docs/modding/guide/enemies.md)):

- `ChanceToDropGrenade`: `Hardcore` `0.33` -> `0.45`, `Infamous` `0.25` -> `0.35` (`Casual` `1` and
  `Experimented` `0.5` unchanged).
- `ClipMultiplierForPickup`: `Casual` `2` -> `1` (`Experimented` `1`, `Hardcore` `0.5`, `Infamous`
  `0.25` unchanged).

## Uncertain

- That these are read per difficulty setting, as the attribute names say, is the guide's reading,
  not traced.
