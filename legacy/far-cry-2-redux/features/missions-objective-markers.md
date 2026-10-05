---
title: Objective markers - no map arrows, a GPS arrow to buddy objectives, no GPS safehouses
kind: component
bundle: navigation
status: located
systems: [missions, ui]
match:
  - "generated/entitylibrarypatchoverride.fcb/domino/objectives/*.xml#**"
exclude: []
requires: []
verified: diff
---

# Objective markers - no map arrows, a GPS arrow to buddy objectives, no GPS safehouses

The objective entities themselves choose which markers they put on the paper map and the GPS. The
mod changes five of them: the paper map stops drawing an arrow toward the current mission, buddy
and underground objectives; buddy (partner) mission objectives get a direction arrow on the GPS;
unlocked safehouses no longer appear on the GPS. The same in every variant. No readme line names
it.

## How

`generated/entitylibrarypatchoverride.fcb/domino/objectives/`, copies of the `worlds/world1` and
`worlds/world2` declarations, `CMapElementComponent/Markers`, 7 values:

| Archetype (`Objectives.…`) | Field | Vanilla | Mod |
|---|---|---|---|
| `MissionObjective` | `archMapDir` | `gadgets.ObjectiveIcons.MissionArrow` | empty |
| `MissionSubvertAnte` | `archMapDir` | `…SubvertArrow` | empty |
| `GRINObjective` | `archMapDir` | `…UnderGroundArrow` | empty |
| `PartnerMissionObjective` | `archCompassDir` | empty | `…MissionArrowGPS` |
| `PartnerMissionObjective` | `archCompassVehicle` | `…PartnerMissionObjective_GPS_VEH` | `…MissionArrowGPS_VEH` |
| `UnLockedSafeHouse` | `archCompass`, `archCompassVehicle` | `…SafeHouse_Unlocked_GPS`, `…_GPS_VEH` | empty |

The other marker slots (`archMapEnabled`, the GPS markers and arrows of the main, buddy and
underground objectives) are unchanged, so those objectives still show on the map and the GPS.

## Relation to the navigation pages

- The map arrows: the "Player position on map" variant also blanks the three arrow models
  (`nav-no-player-position`). Since the objectives no longer name the arrows in either variant,
  the analysed variant draws no map arrows either, through this page.
- The GPS safehouses: `nav-no-gps-safehouses` breaks the GPS safehouse models; this page also stops
  unlocked safehouses from placing them. Either alone removes the unlocked-safehouse marker; the
  locked one goes only through the broken model.
- Realism Plus removes the same three map arrows by emptying their models
  ([`nav-objective-arrows-removed`](../../realism-plus/features/nav-objective-arrows-removed.md)).

## Uncertain

- That `archMapDir` is the map's off-screen arrow toward the objective and `archCompassDir` the
  GPS's is read from the field names and the arrow archetypes they name; where each draws is not
  traced.
- On foot a buddy objective keeps its own GPS marker and gains the arrow; in a vehicle its marker
  becomes the mission arrow itself. Whether that is intended is not stated.
