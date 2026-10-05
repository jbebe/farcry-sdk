---
title: Map and GPS markers do not blink
kind: component
bundle: navigation
claims: []
status: located
systems: [ui]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/{compasssingle,compass_vehicle}.xml#Entity/Components/CFCXCompassObjectives/MarkerBlinking/*"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/map.xml#Entity/Components/CGadget/UseStrategy/MarkerBlinking/MarkerTypes/Objective"
exclude: []
requires: []
verified: diff
---

# Map and GPS markers do not blink

New and updated objective markers stay steady on the paper map and the GPS instead of flashing.
This is the "map icons no longer blinking" of the mod's description; not a line of the readme.

## How

`generated/entitylibrarypatchoverride.fcb`, copies of the `worlds/world1` declarations:

- `gadgets.Equipped.CompassSingle` and `gadgets.Equipped.Compass_Vehicle` (the GPS on foot and in a
  vehicle), `CFCXCompassObjectives/MarkerBlinking`: `fMarkerBlinkFrequency` 0.3 -> 0,
  `fMarkerBlinkDuration` 3 -> 0.
- `gadgets.Equipped.Map`, `CGadget/UseStrategy/MarkerBlinking/MarkerTypes/Objective` `True` ->
  `False`: objective markers are no longer among the map's blinking marker types.

## Player position on map variant

That variant keeps all three archetypes' base blinking values; see `nav-no-player-position`.

## Depends on

Nothing. The bundled Multi-Fixer's "No Blinking Items" (`engine-multi-fixer`) also lists map icons
among what it stops blinking; the two overlap.

## Uncertain

- That a zero frequency and duration means "no blink" rather than "blink once" is inferred from the
  values.
