---
title: Muzzle flash lights the ground on Very High terrain
kind: component
bundle: graphics
status: located
systems: [graphics, weapons]
match:
  - "engine/settings/defaultrenderconfig.xml#Terrain/quality[veryhigh]@TerrainAffectedByMuzzleFlash"
exclude: []
requires: []
verified: diff
---

# Muzzle flash lights the ground on Very High terrain

With terrain quality on Very High, the flash of a shot lights the ground around the shooter, as it
already did on Ultra High.

## How

`engine/settings/defaultrenderconfig.xml`, `Terrain/quality[veryhigh]`: `TerrainAffectedByMuzzleFlash`
0 -> 1.

## Uncertain

- The effect is read from the key's name; the Ultra High level already had it at 1 in the base
  game.
