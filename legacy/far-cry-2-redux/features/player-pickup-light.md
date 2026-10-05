---
title: Dimmer pickup light
kind: component
bundle: gameplay
claims: []
status: located
systems: [player, graphics]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer/*.xml#**/PickupLight/fIntensity"
exclude: []
requires: []
verified: diff
---

# Dimmer pickup light

The light the player character carries for picking things up glows at a quarter of its strength.

## How

`CPlayerSoundAndFXComponent/PickupLight/fIntensity` `1` -> `0.25` on the twelve
`player.MainCharacter.PawnPlayer.<character>` archetypes, the mod's copies in
`generated/entitylibrarypatchoverride.fcb` (the bare `PawnPlayer` is not changed).

## Uncertain

- What `PickupLight` lights (a glow on the item in reach, or on the player's hands) is not traced.
  Not a line of the readme.
