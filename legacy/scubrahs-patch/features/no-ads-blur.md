---
title: No iron-sight blur
kind: component
bundle: visuals
claims:
  - "Disabled ADS/iron sight blur effect"
status: located
systems: [weapons, graphics]
match:
  - "worlds/*/generated/moviedata.xml#SequenceData/Sequence[{52,53}]/**"
exclude: []
requires: []
verified: diff
---

# No iron-sight blur

Aiming down the sights no longer blurs the edges of the view.

## How

The blur is a post effect played as a movie sequence. In `worlds/world1/generated/moviedata.xml` and
`world2`'s, the sequences `PostFx.Primary.In` (index 52) and `PostFx.Primary.Primary` (53) each lose
their one track, `ParamId="15"` with the texture `graphics\PostFX\Ironsight.png`. The sequences stay, so
the engine still plays them on aiming, but they animate nothing.

The weapons' own `IronSight/IronsightFX` values and the `PostFXs.PostFXs.Database` archetype are
untouched.

## Uncertain

- That index 52 and 53 are the only sequences aiming plays is read from their names.
