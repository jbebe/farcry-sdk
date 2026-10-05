---
title: Steeper slopes can be climbed
kind: component
bundle: gameplay
claims: []
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer{,/*}.xml#**/CharacterParams/{fMaxSlope,fMaxTerrainSlope}"
exclude: []
requires: []
verified: diff
---

# Steeper slopes can be climbed

The player walks up steeper ground and objects before sliding back.

## How

`CCharacterPhysComponent/CharacterParams`: `fMaxSlope` `60` -> `70` and `fMaxTerrainSlope` `45` ->
`55` (degrees) on `player.MainCharacter.PawnPlayer` and eight characters that set their own values
(Andre Hyppolite, Flora Guillen, Frank Bilders, Hakim Echebbi, Josip Idromeno, Marty Alencar,
Michele Dachss, Nasreen Davar); the other four do not carry the fields.

## Depends on

Nothing. Realism Plus raises the terrain slope to `54`
([`player-slope-climbing`](../../realism-plus/features/player-slope-climbing.md)).

## Uncertain

- Whether Paul Ferenc, Quarbani Singh, Warren Clyde and Xianyong Bai inherit the new values from
  `PawnPlayer` or keep built-in defaults is not traced. Not a line of the readme.
