---
title: Player health cut by 15%
kind: component
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/curves/playersicknesscurves/health{max,barsize}_*.xml#**"
exclude: []
requires: []
verified: diff
---

# Player health cut by 15%

The player has 15% less health on every difficulty, spread over the same number of health bars.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`; both knots' `Value.y` of each curve
are multiplied by 0.85:

| Difficulty | `HealthMax_*` | `HealthBarSize_*` |
|---|---|---|
| Casual | `1800/1300` -> `1530/1105` | `360/260` -> `306/221` |
| Experienced | `1400/1025` -> `1190/872` | `280/205` -> `238/175` |
| Hardcore | `1075/825` -> `914/702` | `215/165` -> `183/141` |
| Infamous | `825/700` -> `702/595` | `165/140` -> `141/119` |

The guide says both curves must change by the same proportion
([player character](../../../docs/docs/modding/guide/player-character.md#healthhealing)).

## Uncertain

- The published list does not mention it; what the two knots of each curve stand for (health at the
  start and end of a range of progress, inference) is not traced.
