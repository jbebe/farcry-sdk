---
title: Inspect the weapon
kind: component
bundle: gameplay
claims:
  - "Added the ability to inspect your weapon (press I)"
status: located
systems: [weapons, input]
match:
  - "config/defaultusercontrols.xml/category_weapons.xml#Control[cyclebreaker]"
  - "config/inputactionmapsingle.xml/weapons.xml#Binding[+5]"
  - "scripts/engine/objects/pawn/statemachine/main_avatar.gosm.xml#State[3]/Sink[1]"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[7]/StateRef[7]"
  - "languages/*/oasisstrings.fragment.xml#Actions/cyclebreaker"
exclude: []
requires: []
verified: diff
---

# Inspect the weapon

Pressing I plays the idle "look at the gun" animation on demand.

## How

The animation is the base game's idle cycle breaker, `Main Avatar/Common/IdleCycleBreaker`, entered
on the signal `cyclebreaker` that the engine sends after a spell of idling.

- `inputactionmapsingle.xml` `ActionMap[weapons]` binds `kb:i` -> `cyclebreaker`;
  `defaultusercontrols.xml` `CATEGORY_WEAPONS` adds `Control[cyclebreaker]` (`kb:i`, actionmap
  `common_weapons_remap`), labelled `Actions/cyclebreaker` "Inspect Weapon" in all nine languages.
- `main_avatar.gosm.xml` `IdleCycleBreaker` loses its `abort breaker` sink (signal `abort` back to
  `Common/Idle`), so the animation is not cut short.
- `weapons.gosm.xml` `AllowIronSight` loses `xIdleCycleBreaker`, so aiming down the sights is not
  offered while it plays.
