---
title: Malaria pill animation shows the count
kind: component
status: located
systems: [player, graphics]
match:
  - "graphics/characters/_common/animations/healing/malaria/1stge_fulb_takepillsgeneric_nodir_nowep_i1.mab"
exclude: []
requires: []
verified: diff
---

# Malaria pill animation shows the count

The generic first-person pill-taking animation is replaced by one of the game's own pill-count
animations.

## How

The mod's `patch.dat` carries
`graphics/characters/_common/animations/healing/malaria/1stge_fulb_takepillsgeneric_nodir_nowep_i1.mab`
(the base game's is 41,248 bytes) as a byte-identical copy of the base game's
`1stge_fulb_takepillscount2_nodir_nowep_i1.mab` from the same folder (47,872 bytes). Whole file; take
it from the archive.

## Uncertain

- When the game plays the generic animation rather than a count-specific one, and so what the swap
  changes on screen, is not traced. Nothing in the published list names it.
