---
title: Malaria collapse without the empty pill bottle
kind: component
bundle: fixes
claims:
  - "The player no longer already has an empty bottle of pills at the beginning of the game."
status: located
systems: [player, graphics]
match:
  - "graphics/characters/_common/animations/healing/malaria/1stge_fulb_falldie_nodir_nowep_i1.mab"
exclude: []
requires: []
verified: diff
---

# Malaria collapse without the empty pill bottle

The first-person malaria collapse is replaced by the plain fall the buddy-rescue sequence uses, so
the player no longer goes down holding a pill bottle before ever having had pills.

## How

`graphics/characters/_common/animations/healing/malaria/1stge_fulb_falldie_nodir_nowep_i1.mab`
(127,776 bytes in the base game) is replaced by a byte-identical copy of the base game's
`special_actions/buddy_rescue/1stge_fulb_desertfalling_nodir_nowep_i1.mab` (66,816 bytes). Whole
file; take it from the archive's `patch.dat`.

## Depends on

Nothing.

## Uncertain

- That the base clip shows the pill bottle, and that the opening's collapse plays it, are inferred
  from the readme's 1-14-21 line and the clip's name; the clip is not decoded. Every later malaria
  collapse (out of pills) plays the new clip too.
- Realism Plus swaps a different malaria clip (the generic pill-taking one,
  [`player-malaria-pill-animation`](../../realism-plus/features/player-malaria-pill-animation.md)).
