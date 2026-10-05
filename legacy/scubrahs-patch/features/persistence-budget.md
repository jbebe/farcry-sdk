---
title: Larger persistence budget
kind: component
status: located
systems: [engine, world]
match:
  - "engine/gamemodes/gamemodesconfig.xml#**/PersistenceMgr/Properties@PersistenceBudget"
exclude: []
requires: []
verified: diff
---

# Larger persistence budget

The world keeps three times as many persistent objects before it starts discarding them. The published
list does not mention this.

## How

`engine/gamemodes/gamemodesconfig.xml`, `PersistenceMgr/Properties@PersistenceBudget` `1000` ->
`3000`.

## Uncertain

- Which objects count against the budget (dropped weapons, bodies, destroyed props) and whether it
  affects save size are not traced.