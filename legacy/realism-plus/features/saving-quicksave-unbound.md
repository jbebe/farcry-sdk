---
title: Quicksave and quickload keys removed
kind: component
bundle: saving
status: located
systems: [save, input]
match:
  - "config/defaultusercontrols.xml/category_misc.xml#Control[{quicksave,quickload}]"
  - "config/inputactionmapsingle.xml#ActionMap[quicksaveload]"
  - "config/inputactionmapsingle.xml#ActionMap[menus]/Import[2]"
exclude: []
requires: []
verified: diff
---

# Quicksave and quickload keys removed

Limited Saving, the analysed variant: F5 no longer quicksaves and F9 no longer quickloads, and the two
controls disappear from the controls menu.

## How

- `config/defaultusercontrols.xml`, `CATEGORY_MISC`: `Control[quicksave]` (`kb:f5`) and
  `Control[quickload]` (`kb:f9`), both on actionmap `quicksaveload_remap`, are removed.
- `config/inputactionmapsingle.xml`: the whole `ActionMap[quicksaveload]` (its
  `quicksaveload_remap` import and the `kb:f5` -> `quicksave` and `kb:f9` -> `quickload` release
  bindings) is removed, and so is `ActionMap[menus]`'s `Import` of it. The importer takes this file
  whole, because an override cannot drop fragments.

This is the input half of Limited Saving; the pause-menu save goes through the `Dunia.dll`
(`saving-no-pause-save`).

## Save Anywhere

The Save Anywhere variants differ from this one only in these two files and in `Dunia.dll`. Their
`defaultusercontrols.xml` keeps both controls (`quicksave` `kb:f5`, `quickload` `kb:f9`, actionmap
`quicksaveload_remap`, group 3, conflict mask 3), and their `inputactionmapsingle.xml` keeps the
`quicksaveload` action map with its two bindings and the `menus` map's import of it, exactly as the
base game has them. The rest of both files is identical to this variant's.

## Uncertain

- Quickload goes with quicksave even though the mod's notes speak only of quicksave; loading the
  last save from the menu still works.
