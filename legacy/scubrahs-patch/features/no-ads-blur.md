---
title: No iron-sight blur
kind: component
bundle: visuals
claims:
  - "Disabled ADS/iron sight blur effect"
status: unresolved
systems: [weapons, graphics]
match: []
exclude: []
requires: []
verified: diff
---

# No iron-sight blur

Aiming down the sights no longer blurs the edges of the view.

## How

No data change found; likely one of the Dunia.dll patches (pending trace).

Searched: no weapon's `IronSight/IronsightFX` changes (the only iron-sight value the mod touches is
`fIronsightFOV`, see `ads-fov`), the post effects in `PostFXs.PostFXs.Database`
(`PostFX.Primary.In` and the rest) are untouched, and no render setting or script mentions a blur.
