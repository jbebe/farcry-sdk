---
title: Three cut missions registered again
kind: component
bundle: gameplay
status: located
systems: [missions, buddies]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/**"
exclude: []
requires: []
verified: diff
---

# Three cut missions registered again

Three missions the shipped game comments out of its mission list are listed again.

## How

`engine/gamemodes/gamemodesconfig.xml`, `MissionManagerService` → `MissionManagement`. Vanilla
carries these three as comments, each with a note. The mod adds them as live `Item`s:

| Item | Vanilla note | Mod |
|---|---|---|
| `StoryMission` `A3SM16` `NobleSacrifice` | "merged in A3SM15" | `act="3"`, `diamondreward="0"`, `buddyunlock="0"` |
| `BuddyMission` `A1BU08` `MountainCessna` | "teleported mission" | `act="1"`, `faction="Blue"` |
| `BuddyMission` `A2BU05` `BlackMamba` | "discover to unlock" | `act="2"`, `faction="Blue"` |

Their Domino graphs ship in the retail `common` archive:
`a3sm16_noblesacrifice.a3sm16_briefing.lua`, `a1bu08_mountaincessna.a1bu08_mission.lua` and
`a2bu05_blackmamba.a2bu05_mission.lua`. [The guide](../../../docs/docs/modding/guide/misc.md)
covers swapping Black Mamba in for another buddy rescue.

## Uncertain

- Registering a mission is not the same as making it reachable. Nothing else in this mod gives the
  three a trigger, a giver or mission layers in the world, and whether any of them can start in a
  campaign has not been checked.
