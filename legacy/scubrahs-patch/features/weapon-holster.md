---
title: Holster the weapon
kind: component
bundle: gameplay
claims:
  - "Added the ability to holster your weapon (press Y)"
status: located
systems: [weapons, input]
match:
  - "config/defaultusercontrols.xml/category_weapons.xml#Control[customholster]"
  - "config/inputactionmapsingle.xml/weapons.xml#Binding[+6]"
  - "scripts/engine/objects/pawn/statemachine/main_avatar.gosm.xml#State[2]/Event"
  - "languages/*/oasisstrings.fragment.xml#Actions/customholster"
exclude: []
requires: []
verified: diff
---

# Holster the weapon

Pressing Y puts the weapon away, leaving empty hands.

## How

- `inputactionmapsingle.xml` `ActionMap[weapons]` binds `kb:y` -> `customholster`;
  `defaultusercontrols.xml` `CATEGORY_WEAPONS` adds `Control[customholster]` (`kb:y`, actionmap
  `common_weapons_remap`), labelled `Actions/customholster` "Holster Weapon" in all nine languages.
- `main_avatar.gosm.xml` `Common/Idle` gains an event `Holster` on `customholster`:
  `CGOStateEventInventory`, `requestType` `4` (the holster request the bed's `HolsterAbort` state
  uses).

## Uncertain

- Boggalog's guide pairs a holster key with edited holster animations for twelve weapons whose arms
  otherwise stay raised; this mod ships none of those `.mab` files, so those weapons likely keep
  that glitch.
