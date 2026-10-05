---
title: Brighter, longer muzzle flashes
kind: component
bundle: gameplay
status: located
systems: [weapons, graphics, ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultMuzzleFlashManager@*"
exclude: []
requires: []
verified: diff
---

# Brighter, longer muzzle flashes

`engine/gamemodes/gamemodesconfig.xml`, `DefaultMuzzleFlashManager`, the property block of the
`CMuzzleFlashManager` service every mode runs:

| Attribute | Vanilla | Mod |
|---|---|---|
| `clrColor` | `1,1,1` | `2,2,1` |
| `fIntensity` | `1` | `7` |
| `fRadius` | `2.f` | `1.f` |
| `fLifeSpan` | `0.06` | `0.1` |
| `fTimeFlushHistory` | `20.f` | `50.f` |
| `iMaxHistory` | `20` | `50` |

Read by name, the first four set the dynamic light a shot throws: much brighter, warmer, tighter
and on for longer. The last two keep more flashes, for longer, in the manager's history.

## Uncertain

- The block is not traced. What the history feeds, perhaps the AI noticing gunfire by its flash,
  has not been checked, and neither has how a radius of 1 looks against vanilla's 2.
