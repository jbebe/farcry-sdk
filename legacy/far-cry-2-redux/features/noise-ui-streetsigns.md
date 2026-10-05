---
title: Street sign archetypes the game never places
kind: noise
status: located
systems: [world]
match:
  - "generated/entitylibrarypatchoverride.fcb/oa_streetsigns/streetsigns/*/multi.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/oa_streetsigns/streetsigns/{urbanstreetsignnoparking,urbanstreetsignwalk}.xml"
  - "generated/entitylibrarypatchoverride.fcb/oa_streetsigns/missionobjectivesigns/sehlakalasesign.xml"
exclude: []
requires: []
verified: diff
---

# Street sign archetypes the game never places

Street-sign changes with no effect in the single-player game.

## Why it is noise

- **Re-encoded multiplayer signs (48 changes).** The 24 `StreetSigns.*.Multi` archetypes that
  Steam's own override library declares (`CountryStreetSign*`, `UrbanStreetSign*`,
  `CrossRoadSign_BK`, `ForkRoad_BK`, `TeaRoad_BK`, `SafeHouseSign`) come back from the mod's tool
  with `disEntityId` renumbered and without the editor flag `CGraphicComponent/bIntelHackGliderOn`
  (`False`). Multiplayer and map-editor archetypes; single-player sectors do not place them.
- **Three new archetypes (3 whole units).** `StreetSigns.UrbanStreetSignNoParking`,
  `StreetSigns.UrbanStreetSignWalk` and `MissionObjectiveSigns.SehlakalaseSign` (plain white
  colours, like `nav-plain-road-signs`) are declared in no base library, and no sector of the mod
  or the base game names them, so nothing spawns them.

These are the same three new archetypes and the same `.Multi` re-encoding as
`noise-ui-streetsigns` in `legacy/realism-plus`.

## Uncertain

- Where the three new archetypes come from (an editor library or a shared source) is not known.
