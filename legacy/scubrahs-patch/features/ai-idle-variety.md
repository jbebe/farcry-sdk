---
title: Enemy idle activity mix
kind: component
bundle: gameplay
claims:
  - "Tweaked enemy idle behavior probabilities for more variety"
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/FCXAIBehaviorService/{FreeFireZoneSTPProbabilities,SocialZoneSTPProbabilities}/**"
exclude: []
requires: []
verified: diff
---

# Enemy idle activity mix

Off-duty mercenaries spread their time more evenly between standing guard, resting and socialising,
instead of nearly all chatting by day and nearly all resting at night.

## How

`engine/gamemodes/gamemodesconfig.xml`, `FCXAIBehaviorService`: the chance (percent) that an idle
merc picks a duty, rest or social point, per time of day. `FreeFireZoneSTPProbabilities` and
`SocialZoneSTPProbabilities` get the same new table:

| Time of day | Base game duty / rest / social | Mod |
|---|---|---|
| `MidnightToSunrise` | 20 / 70 / 10 | 25 / 50 / 25 |
| `SunriseToNoon` | 15 / 15 / 70 | 45 / 10 / 45 |
| `NoonToSunset` | 20 / 10 / 70 | 40 / 20 / 40 |
| `SunsetToMidnight` | 30 / 30 / 40 | 25 / 50 / 25 |

## Uncertain

- "STP" is read as smart terrain point, the places mercs go to idle; inferred from the names.
