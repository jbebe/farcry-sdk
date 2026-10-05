---
title: Fast travel takes 2 hours
kind: component
bundle: gameplay
claims:
  - "Fast travel now forwards in-game time by 2 hours instead of 4"
status: located
systems: [missions, environment]
match:
  - "domino/user/fasttravel/fasttravel.fasttravel.lua@L2468"
exclude: []
requires: []
verified: diff
---

# Fast travel takes 2 hours

A bus ride moves the clock forward 2 hours instead of 4.

## How

`domino/user/fasttravel/fasttravel.fasttravel.lua@L2468`: after the trip, the graph's
`SetTimeOfDay` box is fired on `IncrementTimeOfDay` with `Hour` `4` -> `2`.

## Uncertain

- The other 40 hunks of the same file belong to the optional diamond cost
  (`missions-fast-travel-cost`), not to this feature.
