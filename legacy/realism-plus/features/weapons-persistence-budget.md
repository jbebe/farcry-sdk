---
title: Ten times the persistence budget
kind: component
status: located
systems: [engine, world]
match:
  - "engine/gamemodes/gamemodesconfig.xml#**/PersistenceMgr/Properties@PersistenceBudget"
exclude: []
requires: []
verified: diff
---

# Ten times the persistence budget

The world keeps ten times as many persistent objects before it starts discarding them.

## How

`engine/gamemodes/gamemodesconfig.xml`, `PersistenceMgr/Properties@PersistenceBudget` `1000` ->
`10000`. Scubrah's Patch raises the same value to `3000`
([`persistence-budget`](../../scubrahs-patch/features/persistence-budget.md)).

## Uncertain

- Which objects count against the budget (dropped weapons, bodies, destroyed props) and whether it
  affects save size are not traced.
