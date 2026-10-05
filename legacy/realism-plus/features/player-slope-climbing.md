---
title: Steeper slopes can be climbed
kind: component
bundle: gameplay
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/**#**/fMaxTerrainSlope"
exclude: []
requires: []
verified: diff
---

# Steeper slopes can be climbed

The player walks up slopes a fifth steeper before sliding back.

## How

`CCharacterPhysComponent/CharacterParams/fMaxTerrainSlope` `45` -> `54` (degrees) on the twelve
`player.MainCharacter.PawnPlayer.<character>` archetypes, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`
([player character](../../../docs/docs/modding/guide/player-character.md#slope-climbing-ability)).
