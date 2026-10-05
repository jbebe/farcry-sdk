---
title: Holster key (X)
kind: component
bundle: gameplay
status: located
systems: [input, weapons]
match:
  - "config/defaultusercontrols.xml/category_actions.xml#Control[holster]"
  - "config/inputactionmapcommon.xml/common_weapons.xml#{Import[+4],Binding[+7],Binding[+23]}"
  - "languages/*/oasisstrings.fragment.xml#Actions/holster"
exclude: []
requires: []
verified: diff
---

# Holster key (X)

Pressing X (holding Y on a gamepad) puts the weapon away. The key is rebindable in the controls
menu as "Holster".

## How

The input half:

- `config/defaultusercontrols.xml`, `CATEGORY_ACTIONS`: new `Control[holster]`, `key1` `kb:x`,
  actionmap `common_holster_weapons_remap`, group 1, conflict mask 12.
- `config/inputactionmapcommon.xml`, `ActionMap[common_weapons]`: imports
  `common_holster_weapons_remap` (optional), and binds `kb:x` press and `pad:y` hold to the signal
  `HolsterWeapons`.
- `Actions/holster`, the control's label, in all ten languages ("Holster", "Étui", "Kabura"...).

## Depends on

- `weapons-holster`, the state-machine half (`main_avatar.gosm.xml` gains a `HolsterWeapons` sink
  and a `Common/WeaponsHolsteredState`, `weapons.gosm.xml` a `HolsterWeapons` state and four
  references to the holstered state) and the holster animations reworked for twelve weapons.
  Without it the signal does nothing; it requires this page.

## Uncertain

- The gamepad binding is a hold on Y, which the base layout presses for `use` (interact) in other
  action maps; how the two share the button is not checked.
