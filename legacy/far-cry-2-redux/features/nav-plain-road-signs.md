---
title: Road signs no longer light up toward missions
kind: component
bundle: navigation
claims: []
status: located
systems: [ui, world, missions]
match:
  - "generated/entitylibrarypatchoverride.fcb/oa_streetsigns/{missionobjectivesigns,missionobjectivesignssafehouse}/*.xml#Entity/Components/CRoadSign/{Colors,MissionColors}/**"
exclude: []
requires: []
verified: diff
---

# Road signs no longer light up toward missions

Place, shop and safehouse signs along the roads keep their plain colours; they no longer turn red,
blue or yellow to point the way to the active main, buddy or underground objective. Both navigation
variants have it.

## How

56 sign archetypes are copied from `worlds/world1` and `worlds/world2` into
`generated/entitylibrarypatchoverride.fcb`: 43 `OA_StreetSigns.MissionObjectiveSigns.*` (`PalaSign`,
`WeaponShop`, `PostOfficeSign`...) and 13 `MissionObjectiveSignsSafeHouse.SafeHouse_*`. Two edits,
belt and braces:

- **`CRoadSign/Colors`** on all 56, the tint for each kind of objective, set to white:
  `Main` (1, 0, 0, 0), `Subvert` (0, 0.2, 1, 0) and `Underground` (0.85, 0.85, 0, 1) all ->
  (1, 1, 1, 1); `None` is already white. 504 value changes.
- **`CRoadSign/MissionColors`** on 55 (all but `WeaponShop`, which has none): every `MissionColor`
  entry, the colour a sign takes for one mission, has its `selColor` (1 for `Main`, 3 for
  `Underground` and so on) set to 0, `None`. The entry count per sign is unchanged; the analysis
  lists each as removed and re-added (200 + 200 changes).

The same 56 copies carry no other change.

`legacy/realism-plus` `nav-plain-road-signs` makes the same `Colors` edit on the same 56 signs
without touching `MissionColors`.

## Uncertain

- Either edit alone would presumably keep the signs plain; which one the engine reads first is not
  traced.
