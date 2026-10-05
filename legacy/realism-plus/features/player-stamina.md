---
title: Sprinting, swimming and jumping drain less stamina
kind: component
bundle: gameplay
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/curves/playersicknesscurves/stamina{sprint,jump}drain.xml#**"
exclude: []
requires: []
verified: diff
---

# Sprinting, swimming and jumping drain less stamina

The player sprints and swims two and a half times as long, and a jump costs less than half the
stamina.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`:

- `Curves.PlayerSicknessCurves.StaminaSprintDrain`: both knots' `Value.y` `-10` -> `-4`.
- `Curves.PlayerSicknessCurves.StaminaJumpDrain`: both knots' `Value.y` `10` -> `4`.

The single-player `DefaultCountersService` stamina block (unchanged) names `StaminaSprintDrain` for
both `SprintDrain` and `SwimDrain`, and `StaminaJumpDrain` for `JumpDrain`
([player character](../../../docs/docs/modding/guide/player-character.md#stamina)).

## Depends on

Nothing. Scubrah's Patch halves the sprint drain the same way, `-10` -> `-5`
([`player-stamina`](../../scubrahs-patch/features/player-stamina.md)).
