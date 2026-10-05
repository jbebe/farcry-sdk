---
title: Map and GPS show no position, objectives or outposts
kind: component
bundle: navigation
status: located
systems: [ui, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/gadgets/objectiveicons/{compassobjective,compassobjective_veh,compasssubvertante,compasssubvertante_veh,compassundergroundobjective,compassundergroundobjective_veh,guardpost_locked,missionarrowgps,missionarrowgps_veh,partnermissionobjective_gps,partnermissionobjective_gps_veh,playerposition,safehouse_locked_gps,safehouse_locked_gps_veh,safehouse_unlocked_gps,safehouse_unlocked_gps_veh,subvertarrowgps,subvertarrowgps_veh,undergroundarrowgps,undergroundarrowgps_veh}.xml#**"
exclude: []
requires: []
verified: diff
---

# Map and GPS show no position, objectives or outposts

Limited Navigation, the analysed variant. The paper map no longer marks where the player is or
where undiscovered guard posts are, and the GPS no longer shows objective markers, objective arrows
or safehouses. What is left to find the way is the map itself, road signs and the GPS's own player
marker.

## How

`generated/entitylibrarypatchoverride.fcb/gadgets/objectiveicons/`: 20 marker archetypes, copied
from `worlds/world1` into the override library, each lose their one `CGraphicComponent/object`, the
`graphics\objects\mapcompass\icon_*.xbg` model the marker draws. The markers are still placed, but
draw nothing:

| Archetype (`ObjectiveIcons.…`) | Model removed |
|---|---|
| `PlayerPosition` | `icon_playerpos.xbg` |
| `GuardPost_Locked` | `icon_guardpost.xbg` |
| `CompassObjective`, `_VEH` | `icon_baseobjectivecirclegps{,veh}.xbg` |
| `MissionArrowGPS`, `_VEH` | `icon_basearrowgps{,veh}.xbg` |
| `CompassSubvertAnte`, `_VEH` | `icon_subvertobjectivesquaregps{,veh}.xbg` |
| `SubvertArrowGPS`, `_VEH` | `icon_subvertarrowgps{,veh}.xbg` |
| `CompassUndergroundObjective`, `_VEH` | `icon_undergroundobjectivecirclegps{,veh}.xbg` |
| `UnderGroundArrowGPS`, `_VEH` | `icon_undergroundarrowgps{,veh}.xbg` |
| `PartnerMissionObjective_GPS`, `_GPS_VEH` | `icon_partnerobjectivecirclegps{,veh}.xbg` |
| `SafeHouse_Locked_GPS`, `_VEH` | `icon_safehouse_2_gps{,veh}.xbg` |
| `SafeHouse_Unlocked_GPS`, `_VEH` | `icon_safehouse_gps{,veh}.xbg` |

(`_VEH` are the in-vehicle GPS versions.) The map's own objective arrows go in both navigation
variants and are `nav-objective-arrows-removed`; the save-point icons go through the `Dunia.dll`
(`nav-no-save-icons`).

## Full Navigation

The Full Navigation variant's `entitylibrarypatchoverride.fcb` keeps all 20 models: its copies of
these archetypes carry the same `CGraphicComponent/object` as the base game (model, mesh and node
names `Icon_PlayerPos`, `Icon_BaseArrowGPS_LOD0` and so on). So with Full Navigation the map shows
the player and locked guard posts, and the GPS shows objectives, arrows and safehouses as in the
base game.

## Depends on

Nothing. Removing a model is self-contained; the GPS and map gadgets still list these markers in
their `MarkerVisibility`.

## Uncertain

- That `GuardPost_Locked` is the marker for guard posts not yet discovered is read from its name,
  matching the mod's note that undiscovered outposts are hidden.
- The GPS's player marker is not among these archetypes, which fits the note that only the GPS shows
  the player; which archetype draws it is not traced.
