---
title: Off-duty routine odds doubled
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/FCXAIBehaviorService/**"
exclude: []
requires: []
verified: re
---

# Off-duty routine odds doubled

Every routine probability for soldiers in social and free-fire zones is doubled. That looks
harmless, but it changes what idle soldiers do, because the engine does not read the numbers as
weights.

## How

`engine/gamemodes/gamemodesconfig.xml`, `FCXAIBehaviorService` → `SocialZoneSTPProbabilities` and
`FreeFireZoneSTPProbabilities`. Each of the four times of day has `duty`, `rest` and `social`. All
24 values are doubled, for example 15/15/70 becomes 30/30/140 and 20/70/10 becomes 40/140/20.

`CHumanPersonality::GetNeedOrder` (server build) picks a soldier's next need. It takes the three
values for the time of day from `CFCXAIBehaviorService::GetSTPProbabilitySet` and rolls once from
0 to 100. The first value is a threshold, and the first plus the second is the next. The values are
cumulative percents out of 100, not weights. Doubled:

- Each set summed to 100 and now sums to 200, so the last need in the roll only wins if the first
  two together stay under 100. Where they reach 100, the last need is never picked first.
- Where the first two are small, they double and the last gets what remains. If the roll order were
  `duty`, `rest`, `social`, morning in a social zone (15/15/70 becoming 30/30/140) would put social
  first 40 % of the time instead of 70 %. Night (20/70/10 becoming 40/140/20) would never put it
  first.

Which of `duty`, `rest` and `social` comes first in the roll was not mapped. The engine's fallback,
when the service is missing, is 45/45/10.

## Depends on

The block belongs to `CFCXAIBehaviorService`: the campaign's, and the editor mode's with
[`ai-editor-mode-services`](ai-editor-mode-services.md).
