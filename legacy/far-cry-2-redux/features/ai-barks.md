---
title: Enemy barks heard from farther and held longer
kind: component
bundle: gameplay
status: located
systems: [ai, audio]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/SPBarkManagerService/**"
exclude: []
requires: []
verified: diff
---

# Enemy barks heard from farther and held longer

Three timing values of the single-player bark manager, the system that picks what soldiers shout.
Part of the "more verbal enemies" of the mod's description; no readme line names it.

## How

`engine/gamemodes/gamemodesconfig.xml`, `SPBarkManagerService`:

| Setting | Vanilla | Mod |
|---|---|---|
| `GenericModeDuration` | 20 | 35 |
| `BarkMentalStateNearDistance` | 30 | 45 |
| `DisorganizedBarkCensorDuration` | 15 | 20 |

`CFCXBarkManagerService::GetGenericModeDuration` (traced in `FarCry2_server`) returns hard-coded
durations for most stimuli - 20 for noise, near misses and interest, 50 for assault, suppression
and vehicle chases, 20 for the provocation and detection stimuli - and `GenericModeDuration` only
for the stimuli outside those lists. Bark playback itself is described in
[audio runtime](../../../docs/docs/engine-internals/audio-runtime.md).

## Uncertain

- What the "generic mode" is, what `BarkMentalStateNearDistance` measures (read: the distance
  within which a soldier barks about his state of mind) and what `DisorganizedBarkCensorDuration`
  holds back (read: the minimum gap between "disorganized" barks, so a higher value means fewer of
  them) are inferences from the names. Whether the net effect is more barks is not checked in game.
