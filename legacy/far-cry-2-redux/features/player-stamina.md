---
title: Stamina lasts longer
kind: component
bundle: gameplay
claims: []
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/curves/playersicknesscurves/stamina{highdrain,jumpdrain,sprintdrain}.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/StaminaDesertDrainFilters/**"
exclude: []
requires: []
verified: diff
---

# Stamina lasts longer

Sprinting and swimming drain stamina at half the rate, heavy drain at a quarter, a jump costs a
little less, and the desert's extra drain starts sooner and covers more of the map.

## How

- The mod's copies in `generated/entitylibrarypatchoverride.fcb`, both knots' `Value.y` each:
  `Curves.PlayerSicknessCurves.StaminaSprintDrain` `-10` -> `-5`, `StaminaHighDrain` `-20` -> `-5`,
  `StaminaJumpDrain` `10` -> `8`.
- `gamemodesconfig.xml` single-player `DefaultCountersService/StaminaDesertDrainFilters`: the level
  bounds move down, `Level[1]@Max` `29` -> `19`, `Level[2]@Min` `30` -> `20`, `Level[2]@Max` `64` ->
  `58`, `Level[3]@Min` `65` -> `59`.

The single-player stamina block names `StaminaSprintDrain` for sprint and swim and `StaminaHighDrain`
for the high drain ([player character guide](../../../docs/docs/modding/guide/player-character.md#stamina)).

## Depends on

Nothing. Realism Plus (`-4`, `4`) and Scubrah's Patch (`-5`) change the sprint curve the same way
([`player-stamina`](../../realism-plus/features/player-stamina.md),
[`player-stamina`](../../scubrahs-patch/features/player-stamina.md)).

## Uncertain

- What the desert drain filter levels measure (a desert intensity, 0-100) is not traced. Not a line
  of the readme.
