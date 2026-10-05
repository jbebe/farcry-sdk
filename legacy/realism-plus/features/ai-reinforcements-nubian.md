---
title: Reinforcements arrive as black riflemen
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/ReinforcementArchetypes/**"
exclude: []
requires: []
verified: diff
---

# Reinforcements arrive as black riflemen

Soldiers who come as reinforcements are black Africans instead of white.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService/GameplayManagement/ReinforcementArchetypes`:
both soldier entries, `type="redmerc"` and `type="BlueMerc"`, change
`enemy_archetypes.Red_Faction.Assault_Caucasian` -> `enemy_archetypes.Red_Faction.Assault_Nubian`
(read as two removals and two additions). The vehicle entry `vehicle.Land.Rover` is unchanged. This
is the "Enemy ethnicity" reinforcement recipe of Boggalog's guide
([enemies: reinforcements](../../../docs/docs/modding/guide/enemies.md#enemy-ethnicity)).

`Red_Faction.Assault_Nubian` exists in the world libraries (and as the mod's override copy), so the
swap is safe.

## Uncertain

- How often reinforcements come is not changed here; `ShootFlare` (calling them) is on
  `ai-tactics-not-certain`.
