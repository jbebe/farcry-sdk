---
title: Named delay presets in the Delay box
kind: shared
status: located
systems: [missions]
match:
  - "domino/system/delay.lua@*"
exclude: []
requires: []
verified: diff
---

# Named delay presets in the Delay box

A shared change to the stock `Delay` Domino box that lets a script ask for a configured delay by
name instead of a number. Functional Outposts, the armory weapon cooldown and the patrol system all
use it.

## How

`domino/system/delay.lua` (every hunk):

- `Start()` and `Restart()` map `Seconds = "Patrol"`, `"Outpost"` and `"Weapon"` to
  `Globals.MASTER_GameGlobals.PatrolDelay`, `OutpostDelay` and `WeaponRespawnDelay`.
- `Start()` forces any duration that is missing, not positive or above 7200 s to 1 s (vanilla only
  defaulted a missing one).
- Durations above 3600 s run as two chained half-length timers (`Event_TimeElapsed_TwoTimers`,
  `Event_TimeElapsed_TwoTimers_Done`); the script's comment calls 2 hours the current maximum.
- `Restart()` notices when the configured duration changed since the timer was created and rebuilds
  the timer with the new value, so a changed setting applies without a new game.
- Adds `System:Log` lines, drops the commented-out traces and the `DOMINO REFLECTION BOX` header.

## Uncertain

- Why timers above an hour are split is not stated beyond the comment; that the engine's delay
  misbehaves past an hour is an inference.
- The 1..7200 s clamp applies to every `Delay` box in the game, not only the mod's.
