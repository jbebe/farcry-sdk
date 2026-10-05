---
title: World streaming radius
kind: component
bundle: visuals
claims:
  - "Increased world streaming radius"
status: located
systems: [engine, world]
match:
  - "engine/settings/defaultengineconfig.xml#WorldConfig@*"
exclude: []
requires: []
verified: diff
---

# World streaming radius

Entities, landmarks, terrain and their high-detail and physics versions are streamed in for a much
larger ring of sectors around the player, so the world is populated further out.

## How

`engine/settings/defaultengineconfig.xml`, `WorldConfig`. The base game's comment names the fields:
`RingData` is entities, landmarkNear, landmarkFar, terrain, preload; `RingLod` is hi-res, physics.

- `RingData` `4,5,10,20,1` -> `500,500,500,500,1`
- `RingLod` `2,4` -> `500,500`

## Uncertain

- The ring unit (sectors) and whether the engine clamps 500 to the world's size are not traced.
