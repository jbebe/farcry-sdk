---
title: Road signs no longer light up toward missions
kind: component
bundle: navigation
status: located
systems: [ui, world, missions]
match:
  - "generated/entitylibrarypatchoverride.fcb/oa_streetsigns/{missionobjectivesigns,missionobjectivesignssafehouse}/*.xml#Entity/Components/CRoadSign/Colors/**"
exclude: []
requires: []
verified: diff
---

# Road signs no longer light up toward missions

Limited Navigation, the analysed variant. Place and safehouse signs along the roads keep their
plain colours; they no longer turn red, blue or yellow to point the way to the active main, buddy or
underground objective.

## How

56 sign archetypes are copied from `worlds/world1` (31) and `worlds/world2` (25) into
`generated/entitylibrarypatchoverride.fcb`: 43 `OA_StreetSigns.MissionObjectiveSigns.*` (`PalaSign`,
`WeaponShop`, `PostOfficeSign`...) and 13 `MissionObjectiveSignsSafeHouse.SafeHouse_*`. On each,
`CRoadSign/Colors`, the tint a sign takes for each kind of objective it points to, is set to white:

| Colour | Base | Mod |
|---|---|---|
| `None` | (1, 1, 1, 1) | unchanged |
| `Main` | (1, 0, 0, 0) | (1, 1, 1, 1) |
| `Subvert` | (0, 0.2, 1, 0) | (1, 1, 1, 1) |
| `Underground` | (0.85, 0.85, 0, 1) | (1, 1, 1, 1) |

That is 9 changed components per sign, 504 in all. The signs' mission states (`MissionStates`) are
untouched, so the engine still selects a colour; every colour is now plain.

## Full Navigation

The Full Navigation variant's copies of these 56 signs (and of the unplaced `SehlakalaseSign`, see
`noise-ui-streetsigns`) keep the base game's colours above, so its signs do light up toward
objectives.

## Uncertain

- What the fourth (`w`) component does is not traced; it goes to 1 with the rest.
