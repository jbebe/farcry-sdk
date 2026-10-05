---
title: No objective arrows on the map
kind: component
bundle: navigation
status: located
systems: [ui]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/objectiveicons/{missionarrow,subvertarrow,undergroundarrow}.xml#**"
exclude: []
requires: []
verified: diff
---

# No objective arrows on the map

The arrows that point the way to the current mission, buddy (subvert) objective and underground
objective are no longer drawn. Unlike the rest of the navigation changes, this is in both
navigation variants.

## How

`generated/entitylibrarypatchoverride.fcb/gadgets/objectiveicons/`: `ObjectiveIcons.MissionArrow`,
`SubvertArrow` and `UnderGroundArrow`, copied from `worlds/world1`, each lose their
`CGraphicComponent/object` (`icon_basearrow.xbg`, `icon_subvertarrow.xbg`,
`icon_undergroundarrow.xbg`). Their GPS twins are `nav-gps-icons-removed`.

## Full Navigation

The same three models are missing from the Full Navigation variant's override library as well, so
this page holds for both.

## Uncertain

- That these are the paper map's arrows (as opposed to the GPS's, which are the `…GPS` archetypes)
  is read from the names; where they draw is not traced.
