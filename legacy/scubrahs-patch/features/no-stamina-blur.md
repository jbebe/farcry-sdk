---
title: No stamina blur
kind: component
bundle: visuals
claims:
  - "Disabled stamina drain blur effect"
status: unresolved
systems: [player, graphics]
match: []
exclude: []
requires: []
verified: diff
---

# No stamina blur

The screen no longer blurs when the player runs out of stamina.

## How

No data change found; likely one of the Dunia.dll patches (pending trace).

Searched: the effect's data is untouched - the stamina post effect `PostFx.StaminaFX.StaminaWeaknessIn`
/ `StaminaWeaknessLoop` in the `PostFXs.PostFXs.Database` archetype, the curve that drives it
(`Curves.PlayerSicknessCurves.StaminaNearZeroFX`, named by `gamemodesconfig.xml`'s
`PlayerSickness/Stamina`), the player archetypes, `domino/system/postfx.lua` (its change is about
the intro's black screen) and every render and engine setting.
