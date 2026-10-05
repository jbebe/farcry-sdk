---
title: Faster sprint that turns better
kind: component
bundle: gameplay
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/curves/locomotion/sprint.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/player/**#**/fSprintingTurnModifier"
exclude: []
requires: []
verified: diff
---

# Faster sprint that turns better

Sprinting builds up to a top speed about a tenth higher, and turning while sprinting costs much less
speed.

## How

- `Curves.Locomotion.Sprint`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`: knots 3 to
  15 are scaled up progressively, both `x` and `y`: knots 3-4 by 1%, 5-6 by 2%, 7-8 by 4%, 9-10 by
  6%, 11-12 by 8%, 13-15 by 10% (for example `Knot[13]` `1.5322, 6.2996` -> `1.68542, 6.92956`;
  26 values). The curve is the one `archSprintCurve` names in `player.xml`
  ([data recipes](../../../docs/docs/modding/data-recipes.md#player)).
- `CPawn/Body/fSprintingTurnModifier` `0.2` -> `0.5` on the twelve
  `player.MainCharacter.PawnPlayer.<character>` copies: turning while sprinting keeps half the speed
  instead of a fifth ([player character](../../../docs/docs/modding/guide/player-character.md#sprinting-turn-modifier)).

## Uncertain

- Read as speed (`y`) over sprint time (`x`): scaling both stretches the curve, so the late part of a
  sprint is up to 10% faster and reached a little later. Not traced.
