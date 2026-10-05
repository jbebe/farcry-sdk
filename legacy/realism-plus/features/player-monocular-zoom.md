---
title: Monocular zooms in less
kind: component
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/monocular.xml#**"
exclude: []
requires: []
verified: diff
---

# Monocular zooms in less

The monocular shows a wider view.

## How

`gadgets.Equipped.Monocular`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`:
`CGadget/UseStrategy/Zoom/fFOV` `0.2` -> `0.3` (radians, about 11.5 -> 17 degrees: two thirds of the
magnification). Scubrah's Patch widens it further, to `0.4`, and also changes its look speed
([`monocular-sensitivity`](../../scubrahs-patch/features/monocular-sensitivity.md)).
