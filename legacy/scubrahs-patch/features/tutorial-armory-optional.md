---
title: Tutorial armory step optional
kind: component
bundle: gameplay
claims:
  - "Disabled the requirement to grab a weapon from the armory in the tutorial"
status: located
systems: [missions]
match:
  - "domino/user/a1bu00_tutorial.a1bu00_weaponshop.lua@L110"
exclude: []
requires: []
verified: diff
---

# Tutorial armory step optional

The tutorial no longer waits for the player to take a weapon from the armory before moving on.

## How

`domino/user/a1bu00_tutorial.a1bu00_weaponshop.lua@L110`: after the weapon-shop step sets objective
state `A1BU00_09`, the `ObjectiveState` box's `Out` is wired to `f_6_TutorialCompleted` instead of
`f_4_Out`. `f_4_Out` launched in-game tutorial `24` (`LaunchTutorial`) and only its
`TutorialCompleted` led on; now the step completes straight away.

## Uncertain

- That tutorial type `24` is the "take a weapon from the armory" step is inferred from the graph's
  name and the claim.
- The neighbouring hunk `@L116` (sets `TutorialFinished`) belongs to `user-configurable`'s malaria
  setting.
