---
title: Machete key also draws the flare gun (texts)
kind: component
bundle: controls
claims:
  - "Flare gun - Tap 1 twice"
status: located
systems: [ui, weapons]
match:
  - "languages/english/oasisstrings.fragment.xml#Actions/machete"
  - "languages/english/oasisstrings.fragment.xml#Tutorial/{TUTORIAL_WEAPON_MACHETE,TU34_MESSAGE}"
exclude: []
requires: []
verified: diff
---

# Machete key also draws the flare gun (texts)

In English, the controls list and the tutorial say that the machete key draws the flare gun on a
second press. The texts only; the behaviour is on the weapons pages.

## How

`languages/english/oasisstrings.fragment.xml`:

- `Actions/machete`, the control's label: "Machete" -> "Machete and Flare Gun".
- `Tutorial/TUTORIAL_WEAPON_MACHETE`: "Machete" -> "Machete and flare gun".
- `Tutorial/TU34_MESSAGE`: "Press {select_hand_to_hand_weapon} to equip your machete." -> "Press
  {select_hand_to_hand_weapon} once to equip your machete, and twice to switch to the flare gun.",
  and the primary-weapon line loses "like an assault rifle or a sniper rifle".

## Depends on

- The flare gun's move to the hand-to-hand slot, which the weapons pages hold. Without it these
  texts describe something the game does not do.
