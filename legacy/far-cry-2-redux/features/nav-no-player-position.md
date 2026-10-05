---
title: No player marker on the paper map
kind: component
bundle: navigation
claims:
  - "Mod updated to include options for full/short taxi ride and player position on the map"
status: located
systems: [ui, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/map.xml#Entity/Components/CGadget/UseStrategy/{archPlayerMarker,fMarkerPlayerZ}"
exclude: []
requires: []
verified: diff
---

# No player marker on the paper map

"No player position on map (Recommended)", the analysed variant: the paper map no longer shows
where the player stands, so the player has to read their position from roads, signs and terrain.
The GPS still points the way. This page is the map-position half of the readme's line; the taxi
half belongs to the bus-ride pages.

## How

`generated/entitylibrarypatchoverride.fcb`, `gadgets.Equipped.Map` (a copy of the `worlds/world1`
declaration), `CGadget/UseStrategy`:

- `archPlayerMarker` `gadgets.ObjectiveIcons.PlayerPosition` is removed, so the map places no
  player marker;
- `fMarkerPlayerZ` 0.0006 -> 0, the marker's height above the paper, moot without a marker.

The same copy also stops objective markers blinking on the map (`nav-no-blinking-markers`).

## Player position on map variant

The Short Taxi Ride "Player position on map" folder differs from the analysed one only in
`generated/entitylibrarypatchoverride.fcb` (440 bytes smaller), and the Full Taxi Ride pair carries
the same two libraries byte for byte, so the map option lives entirely in that file. Decoded and
compared, the "Player position on map" library differs in six archetypes:

- `gadgets.Equipped.Map` keeps the base values: `archPlayerMarker` `PlayerPosition`,
  `fMarkerPlayerZ` 0.0006 and `MarkerBlinking/MarkerTypes/Objective` `True`. The map shows the
  player, and objective markers on it blink.
- `gadgets.Equipped.CompassSingle` and `Compass_Vehicle` keep `MarkerBlinking`
  `fMarkerBlinkFrequency` 0.3 and `fMarkerBlinkDuration` 3, so GPS markers blink as in the base
  game.
- `gadgets.ObjectiveIcons.MissionArrow`, `SubvertArrow` and `UnderGroundArrow` lose their one
  `CGraphicComponent/object` (`icon_basearrow.xbg`, `icon_subvertarrow.xbg`,
  `icon_undergroundarrow.xbg`). The analysed variant keeps these models, but in both variants the
  objectives no longer name the arrows (`archMapDir` emptied,
  [`missions-objective-markers`](missions-objective-markers.md)), so neither draws the map's arrows
  toward the main, buddy and underground objectives. Blanking the models is redundant there.
- `gadgets.Equipped.Binoculars`, an archetype the mod adds, zooms to `Zoom/fFOV` 0.2 instead of
  the analysed variant's 0.3.

Everything else, including the map icon textures, the GPS safehouse and save icons and the road
signs, is the same in both. So the trade is: with the player shown, markers blink as in the base
game; without, there is no player marker and nothing blinks. Neither variant draws the objective
arrows.

## Uncertain

- The binoculars' zoom difference looks unrelated to the map and may be a leftover between builds.
