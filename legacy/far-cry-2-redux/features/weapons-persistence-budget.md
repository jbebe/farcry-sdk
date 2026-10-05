---
title: Ten times the persistence budget
kind: component
bundle: gameplay
claims: []
status: located
systems: [engine, world]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/PersistenceMgr/Properties@PersistenceBudget"
exclude: []
requires: []
verified: diff
---

# Ten times the persistence budget

The world keeps ten times as many persistent objects (dropped weapons, bodies, wrecks) before it
starts discarding them.

## How

`engine/gamemodes/gamemodesconfig.xml`, `PersistenceMgr/Properties@PersistenceBudget` `1000` ->
`10000`.

## Depends on

Nothing. Realism Plus makes the identical change
([`weapons-persistence-budget`](../../realism-plus/features/weapons-persistence-budget.md)), Scubrah's
Patch raises it to `3000` ([`persistence-budget`](../../scubrahs-patch/features/persistence-budget.md)).

## Uncertain

- Which objects count against the budget and whether it affects save size are not traced. Not a
  line of the readme.
