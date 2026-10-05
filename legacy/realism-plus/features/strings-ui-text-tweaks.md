---
title: Author's sign-off and a shortened Polish label
kind: component
bundle: ui
status: located
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#Generic/HELP_WEBSITE"
  - "languages/polish/oasisstrings.fragment.xml#Actions/interact"
exclude: []
requires: []
verified: diff
---

# Author's sign-off and a shortened Polish label

Two small text edits.

## How

- `languages/english/oasisstrings.fragment.xml`, `Generic/HELP_WEBSITE`: "Need Help? Visit
  www.farcrygame.com/help" -> "Thanks for playing! If you have any feedback please let me know at
  Nexus Mods or Moddb - Tom", the author's note in place of the dead help link.
- `languages/polish/oasisstrings.fragment.xml`, `Actions/interact`: "Interakcja" -> "Intera", the
  interact control's label cut to five letters.

## Uncertain

- The Polish label looks truncated by accident (perhaps by the tool that added the new control
  labels), or shortened to fit the controls list; nothing says which.
