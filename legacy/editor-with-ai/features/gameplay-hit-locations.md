---
title: Harder head and torso hits, softer limbs
kind: component
bundle: gameplay
status: located
systems: [player, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/{DefaultCountersService,MPCountersService}/{HitLocations,HitLocationsHardcore}/**"
exclude: []
requires: []
verified: diff
---

# Harder head and torso hits, softer limbs

Hit-location damage multipliers change in both the single-player and the multiplayer counters.

## How

`engine/gamemodes/gamemodesconfig.xml`:

- `DefaultCountersService` → `HitLocations` (the campaign's, and with this mod the editor's): `Head`
  6.0 → 9.0, `Torso` 1.0 → 1.5, `Arms` 1.0 → 0.5.
- `MPCountersService` → `HitLocations`: `Head` 2.5 → 3.5, `Torso` 1.0 → 1.5, `Arms` 1.0 → 0.5.
- `MPCountersService` → `HitLocationsHardcore`: `Head` 3.0 → 3.5, `Torso` 2.0 → 1.5, `Arms`
  1.75 → 0.5, `Legs` 1.00 → 0.5, `Hands` and `Feet` 0.8 → 0.5.

[Data recipes](../../../docs/docs/modding/data-recipes.md) notes that base damage is not set here.
These multiply it per body part.

## Uncertain

- Whether `DefaultCountersService` multipliers apply to hits on the player, on AI, or both, has not
  been checked.
