---
title: More simulated physics objects and ragdolls
kind: component
bundle: improved-graphics
claims: []
status: located
systems: [engine]
match:
  - "engine/settings/defaultengineconfig.xml#PhysicConfig/**"
exclude: []
requires: []
verified: diff
---

# More simulated physics objects and ragdolls

More loose objects and bodies are simulated at once before the engine starts freezing or removing
them. Not a line of the feature list; grouped with Improved Graphics' "and more" by the analysis.

## How

`engine/settings/defaultengineconfig.xml`, `PhysicConfig/Qualities`. The base game's
`QualitySetting` is `VeryHigh`, so that row is the one in effect. Old -> new:

| Level | `MaxSimplePhysObject0` / `1` | `MaxPawnRagdoll0` / `1` | Other |
|---|---|---|---|
| `VeryHigh` | 30 / 16 -> 150 / 150 | 10 / 10 -> 30 / 30 | |
| `High` | 30 / 16 -> 50 / 50 | 10 / 10 -> 30 / 30 | |
| `Medium` | 30 / 16 -> 50 / 50 | 10 / 10 -> 30 / 30 | |
| `Low` | 15 / 8 -> 50 / 50 | 5 / 5 -> 30 / 30 | `MaxAnimalRagdoll0`/`1` 5 -> 10, `MaxPickup0`/`1` 10 -> 20 |

## Uncertain

- What the `0` and `1` suffixes distinguish, and what happens to objects over the limit, are not
  traced.
