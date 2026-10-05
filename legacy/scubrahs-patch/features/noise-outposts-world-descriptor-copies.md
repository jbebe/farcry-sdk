---
title: Stray copies of the modded world descriptors
kind: noise
status: located
systems: [missions]
match:
  - "_hash/{a2240722,fa8791e2}.rml"
exclude: []
requires: []
verified: diff
---

# Stray copies of the modded world descriptors

Two nameless binary-XML files are whole copies of the mod's own world descriptors, stored under paths
the base game does not have.

- `_hash/a2240722.rml` decodes to a `WorldDescriptor` for world 1 (`Levels\W1_*` maps) with 1,515
  missions: vanilla `world1.game.xml`'s 840 plus the mod's 675 (561 ghost patrols, 32 outposts,
  82 weapons-bazaar missions).
- `_hash/fa8791e2.rml` is the same for world 2: 1,422 missions, vanilla's 804 plus the mod's 618.

The missions they hold reach the game through the mod's real `world1.game.xml` / `world2.game.xml`,
whose additions are claimed by the features that use them (`functional-outposts`, the patrol and
weapons-bazaar pages). These copies sit at paths vanilla never ships, so nothing is expected to load
them (inference; the load path was not traced).
