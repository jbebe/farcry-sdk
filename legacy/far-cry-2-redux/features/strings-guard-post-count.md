---
title: Guard post total 56
kind: component
bundle: gameplay
claims: []
status: located
systems: [ui, world]
match:
  - "languages/english/oasisstrings.fragment.xml#PGPs/PGPUnlockedSwoosh"
exclude: []
requires: [world-pala-guard-posts-removed]
verified: diff
---

# Guard post total 56

In English, the "Guard Posts scouted" counter counts to 56 instead of 57.

## How

`languages/english/oasisstrings.fragment.xml`, `PGPs/PGPUnlockedSwoosh`: "NUM_REPLACE/57 Guard
Posts scouted" -> "NUM_REPLACE/56 Guard Posts scouted". The total is part of the text, not
computed.

## Depends on

- The mod removes one guard post from the world: in `levels/w1_c_3`, `worldsector3315` deletes
  `PGP.FuelPile_160` and its `missions\_disableformission\pgp_ai` layer. That is on the world pages;
  this text only matches it.

## Uncertain

- Other languages keep 57.
