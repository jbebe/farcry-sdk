---
title: Saving works again after A1SM01 and A2SM08
kind: component
bundle: fixes
claims:
  - "Fixed a case where the player could no longer save their game after completing certain missions"
status: located
systems: [missions, save, ui]
match: []
exclude: []
requires: [missions-mission-completed]
verified: diff
---

# Saving works again after A1SM01 and A2SM08

After the town escape (`A1SM01`) and the world 2 story mission `A2SM08`, the HUD could stay in
cinematic mode, which blocks saving. The mod resets it when either mission completes.

## How

The fix is two lines inside the large inserted hunk `domino/system/missioncompleted.lua@L32`, which this
page does not claim because the hunk is one change shared by several features; it is claimed by
`missions-mission-completed`. In the `MissionCompleted` box's `In`, commented "Update 3.7: Fix SetHudMode
getting stuck in Cinematic mode after certain missions (preventing the player from saving the game)",
the hunk calls `SetCinematicUIMode(1)` when the completed mission is `A1SM01` or `A2SM08`.

## Depends on

- `missions-mission-completed`, which holds the hunk. Picking this page pulls the whole hunk: buddy unlock
  mission icon clean-up, the airport enemies fix, the buddy side-quest completion branch (reward,
  history, golden AK-47 count) and contextual "mission concluded" messages.

## Uncertain

- That `SetCinematicUIMode(1)` restores the normal HUD (rather than entering cinematic mode) is taken
  from the comment; the function was not traced.
- Neither `savepoints.savepoints.lua` nor `saveafterteleport.lua` is part of this fix; their changes are
  on `concurrent-missions`, `missions-safehouse-buddy-spawns` and `no-popups`.
