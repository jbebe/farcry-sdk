---
title: Slower sprint stamina drain
kind: component
bundle: balancing
claims:
  - "Increased player stamina"
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/curves/playersicknesscurves/staminasprintdrain.xml{,#**}"
exclude: []
requires: []
verified: diff
---

# Slower sprint stamina drain

Sprinting and swimming drain stamina at half the rate, so the player runs and swims twice as long.

## How

`generated/entitylibrarypatchoverride.fcb` gains a copy of
`Curves.PlayerSicknessCurves.StaminaSprintDrain`, which the base game keeps only in the world
libraries. Its two knots' `Value.y` go from `-10` to `-5`.

The single-player `DefaultCountersService/PlayerSickness` in `gamemodesconfig.xml` (unchanged) names
this curve for both sprinting and `SwimDrain`.
