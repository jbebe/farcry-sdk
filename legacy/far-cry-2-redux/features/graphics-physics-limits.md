---
title: More simulated physics objects and ragdolls
kind: component
bundle: graphics
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

Twice as many loose objects and bodies are simulated at once before the engine starts freezing or
removing them, and more pickups stay physical. Not a line of the readme.

## How

`engine/settings/defaultengineconfig.xml`, `PhysicConfig/Qualities/quality[VeryHigh]`, the level
the base game's `QualitySetting` selects:

| Key | Base | Mod |
|---|---|---|
| `MaxSimplePhysObject0` / `1` | 30 / 16 | 60 / 32 |
| `MaxPawnRagdoll0` / `1` | 10 / 10 | 20 / 20 |
| `MaxAnimalRagdoll0` / `1` | 10 / 10 | 20 / 20 |
| `MaxPickup0` / `1` | 20 / 20 | 30 / 30 |

`legacy/scubrahs-patch` `graphics-physics-limits` raises the same keys further (150 objects, 30
ragdolls) and at every level.

## Uncertain

- What the `0` and `1` variants of each key distinguish is not traced.
