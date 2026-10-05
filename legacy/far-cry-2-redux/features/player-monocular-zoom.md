---
title: Monocular zooms less
kind: component
bundle: gameplay
claims: []
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/monocular.xml#**/Zoom/fFOV"
exclude: []
requires: []
verified: diff
---

# Monocular zooms less

The monocular shows a wider view.

## How

`gadgets.Equipped.Monocular`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`:
`CGadget/UseStrategy/Zoom/fFOV` `0.2` -> `0.3`.

## Depends on

Nothing. Realism Plus makes the identical change
([`player-monocular-zoom`](../../realism-plus/features/player-monocular-zoom.md)).

## Uncertain

- Not a line of the readme.
