---
title: No stamina blur
kind: component
bundle: visuals
claims:
  - "Disabled stamina drain blur effect"
status: located
systems: [player, graphics]
match:
  - "worlds/*/generated/moviedata.xml#SequenceData/Sequence[{62,63,64,65,66}]/**"
exclude: []
requires: []
verified: diff
---

# No stamina blur

The screen no longer blurs and darkens when the player runs out of stamina.

## How

The stamina effects are post effects played as movie sequences. In `worlds/world1/generated/moviedata.xml`
and `world2`'s, the five `PostFx.StaminaFX` sequences (indices 62-66: `StaminaIntensityLoop`,
`StaminaWeaknessIn`, `StaminaWeaknessLoop`, `StaminaWeaknessOut`, `Start`) each lose their one
`ParamId="15"` track. The sequences stay, so the engine still triggers them from the stamina curves,
but they animate nothing.

The curve that drives them (`Curves.PlayerSicknessCurves.StaminaNearZeroFX`) and the
`PostFXs.PostFXs.Database` archetype are untouched.
