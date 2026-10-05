---
title: Slower walk
kind: component
bundle: gameplay
claims: []
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer{,/*}.xml#**/CPawn/Body/fWalkingMaxSpeed"
exclude: []
requires: []
verified: diff
---

# Slower walk

The player walks a little slower (sprint is unchanged).

## How

`CPawn/Body/fWalkingMaxSpeed` `3.8` -> `3.5` on the 13 player archetypes, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`
([player character guide](../../../docs/docs/modding/guide/player-character.md)).

## Uncertain

- Not a line of the readme.
