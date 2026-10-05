---
title: Holster and Inspect control labels
kind: component
bundle: controls
claims:
  - "Added key rebinds for holster and inspect in the Options menu"
status: located
systems: [ui, input]
match:
  - "languages/english/oasisstrings.fragment.xml#Actions/{holsterweapon,cyclebreaker}"
exclude: []
requires: []
verified: diff
---

# Holster and Inspect control labels

The controls list in the Options menu names the mod's two new controls "Holster" and "Inspect".
This is the menu text of the readme's 11-9-20 line; the controls themselves are on the input pages.

## How

`languages/english/oasisstrings.fragment.xml`, two new `Actions` strings:
`holsterweapon` "Holster" and `cyclebreaker` "Inspect". They label the controls
`Control[holsterweapon]` (`kb:n`) and `Control[cyclebreaker]` (`kb:i`) that the mod adds to
`config/defaultusercontrols.xml`'s `CATEGORY_ACTIONS`, which is what puts them in the rebinding
list.

## Depends on

- The control and binding entries in `config/defaultusercontrols.xml` and
  `config/inputactionmapcommon.xml` (`common_move` binds `kb:n` to `holsterweapon` and `kb:i` to
  `cyclebreaker`), and the holster and inspect behaviour behind those signals. They are on the
  input and weapons pages; these labels alone add nothing.

## Uncertain

- Only English has the labels; in other languages the list shows the raw key name, if it shows the
  control at all.
