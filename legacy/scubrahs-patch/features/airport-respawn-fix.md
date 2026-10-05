---
title: Airport enemies respawn after the greenhouse mission
kind: component
bundle: fixes
claims:
  - "Fixed a scripting error that caused enemies at the airport to never respawn after completing a story mission"
status: located
systems: [missions, ai]
match: []
exclude: []
requires: [missions-mission-completed]
verified: diff
---

# Airport enemies respawn after the greenhouse mission

The guards at the Leboa-Sako airstrip come back once the greenhouse (grow-op) faction mission is
complete, instead of staying gone for the rest of the game.

## How

Vanilla `a1lm05_growop.a1lm05_mission.lua` switches the layer
`Missions/_DisableForMission/W1D3_A15Airstrip_EnemiesSTP` off (`SetMissionState` `Off` and
`Disable`) while the mission plays, and nothing ever switches it back on.

The mod's `domino/system/missioncompleted.lua` `In()` gains a branch, commented
`FIX: Re-enable airport enemies after greenhouse library mission`: when the completed mission is
`A1LM05` it calls `Enable()` on that mission layer.

## Depends on

The branch is one part of a single inserted hunk (`@L32`) that carries several other features; that
hunk is the shared page `missions-mission-completed`, and this fix has no change of its own beyond it.

## Uncertain

- The published line says "story mission"; the mission is the library (faction) mission `A1LM05`.
