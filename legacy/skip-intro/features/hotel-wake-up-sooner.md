---
title: Shorter hotel wake-up
kind: component
bundle: skip-intro
claims:
  - "Shorter wake-up in the hotel after the briefing"
status: located
systems: [missions]
match:
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{1814,1816}"
exclude: []
requires: []
verified: diff
---

# Shorter hotel wake-up

After the white-out that ends the Jackal's briefing, the mission moves on about 8 seconds sooner.

## How

`domino/user/a1sm01_townescape.a1sm01_mission.lua`, in `f_149_Out`, the wake-up after the
white-out. It plays the cockroach sounds, puts the player on the bed in a looping
`sm01_se01_player_lyingbed_idle2` animation and starts two delays. Both go to 0.1 s:

- `@L1814`: Delay `166`, 8.15 s. When it elapses, `f_166_TimeElapsed` turns off the scene's actors
  layer `Missions/StoryMissions/A1SM01/A1SM01_SE01_Actors` and removes the scripted lighting and
  depth-of-field overrides. Once both are gone, the mission sets objective `A1SM01_02` and the storm
  fog.
- `@L1816`: Delay `169`, 5 s. When it elapses, `f_169_TimeElapsed` stops the `Exclusive.Malaria_SE`
  sound mix and starts the next sounds of the scene.

This works without the other two changes. It shortens the wake-up whether the briefing played in
full or not.

## Uncertain

- How much sooner the player can actually move is not read from the scripts. Nothing on this path
  stops the looping bed animation.
