---
title: Vehicle map and compass sit slightly higher
kind: component
status: located
systems: [ui, vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/compass_vehicle.xml#**"
exclude: []
requires: []
verified: diff
---

# Vehicle map and compass sit slightly higher

The map the player holds while driving is drawn a little higher.

## How

`gadgets.Equipped.Compass_Vehicle`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`:
`CFCXCompassObjectives/Map/fHeightOffset` `0.035` -> `0.039`.

## Uncertain

- Read from the name only; what the offset moves on screen, and why, is not checked. Perhaps it
  pairs with the mod's vehicle camera or field-of-view changes (not checked).
