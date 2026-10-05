---
title: IED on its own slot
kind: component
bundle: gameplay
claims:
  - "The IED now occupies its own weapon slot (press X for quick access)"
status: located
systems: [weapons, input]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/secondary/ied.xml"
  - "config/defaultusercontrols.xml/category_weapons.xml#Control[select_ied]"
  - "config/inputactionmapsingle.xml/weapons.xml#Binding[+4]"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[4]/Event[{+4,+5}]"
  - "languages/*/oasisstrings.fragment.xml#Actions/select_ied"
exclude: []
requires: []
verified: diff
---

# IED on its own slot

The IED no longer takes the secondary-weapon slot, so a pistol or SMG can be carried with it, and X
draws it directly.

## How

- `generated/entitylibrarypatchoverride.fcb` gains a copy of `WeaponProperties.Secondary.IED`, which
  the base game keeps only in the world libraries; it differs only in having no
  `CommonProperties/selCategory` (the base game's is `2`, Secondary, of `Hand To Hand`, `Primary`,
  `Secondary`, `Special`).
- `inputactionmapsingle.xml` `ActionMap[weapons]` binds `kb:x` -> `select_ied`;
  `defaultusercontrols.xml` `CATEGORY_WEAPONS` adds `Control[select_ied]` (`kb:x`, actionmap
  `common_gadget_remap`), labelled `Actions/select_ied` "Draw IED" in all nine languages.
- `weapons.gosm.xml` `WeaponIdleGroup` gains two `CGOStateEventInventory` events on `select_ied`,
  `Select IED 1` (`requestType` `29`) and `Select IED 2` (`requestType` `9`).

## Uncertain

- What category an archetype without `selCategory` lands in, and what inventory requests `29` and
  `9` do, are not traced; read from the names only.
