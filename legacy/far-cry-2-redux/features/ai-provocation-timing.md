---
title: Soldiers react faster to the player crowding, staring at or aiming at them
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/FCXAIBehaviorService/**"
exclude: []
requires: []
verified: diff
---

# Soldiers react faster to the player crowding, staring at or aiming at them

Soldiers who are not yet hostile - in the ceasefire towns, or at the moment they first notice the
player - escalate sooner when the player comes close, stares at them or points a weapon at them,
and they take longer to forget the player afterwards. Part of the "more aggressive enemies" of the
mod's description; no readme line names it.

## How

`engine/gamemodes/gamemodesconfig.xml`, `FCXAIBehaviorService`, 13 values:

| Setting | Vanilla | Mod |
|---|---|---|
| `PlayerAwarenessResetDelay` | 30 | 50 |
| `LongRangeDetectionDurationLevel1` / `Level2` | 2 / 6 | 1.5 / 3 |
| `MediumRangeDetectionDurationLevel1` | 0.5 | 0.8 |
| `PersonalRangeDetectionDurationLevel1` / `Level2` | 3 / 3 | 1 / 1.5 |
| `IntimateRangeDetectionDurationLevel2` | 3 | 1.5 |
| `StareAtDetectionDurationLevel1` / `Level2` | 2 / 4 | 1 / 2 |
| `AimingProvocationDetectionDurationLevel2` / `3` / `4` | 3 / 3 / 3 | 1 / 1.5 / 2 |
| `AimedAtDetectionDurationLevel2` | 4 | 3 |

The range bands are the block's own social distances (`IntimateSocialDistance` 1.5 m,
`PersonalSocialDistance` 5, `MediumSocialDistance` 8, `DistantSocialDistance` 15, unchanged), and
the levels match the escalating provocation stimuli the bark service lists (`PlayerStareL2`/`L3`,
`WitnessAimedWeaponL2`-`L4`, `PlayerIntrudeL2`/`L3`, `AimedWeaponL2`-`L4`). The community reading
of these durations is that a higher value makes a soldier slower to confirm a detection
([data recipes](../../../docs/docs/modding/data-recipes.md)), so all but one change here make him
quicker. `MediumRangeDetectionDurationLevel1` is the exception and goes up.

## Uncertain

- What each duration times, and that the levels are the provocation stages, is read from the names
  and from the stimuli lists in `CFCXBarkManagerService::GetGenericModeDuration`, not traced.
- `PlayerAwarenessResetDelay` read as "seconds before a soldier forgets the player" is an inference
  from the name.
