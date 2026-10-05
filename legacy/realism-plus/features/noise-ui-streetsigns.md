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
  with `disEntityId` four lower (4491 -> 4487 and so on, a renumbering after entries elsewhere in the
  library) and without the editor flag `CGraphicComponent/bIntelHackGliderOn` (`False`). The same
  pattern runs through the other `.Multi` archetypes of the library. These are multiplayer and map
  editor archetypes; single-player sectors do not place them.
- **Three new archetypes (3 whole units).** `StreetSigns.UrbanStreetSignNoParking` and
  `StreetSigns.UrbanStreetSignWalk` (single-player twins of the `.Multi` signs) and
  `MissionObjectiveSigns.SehlakalaseSign` (a mission sign with plain white colours, the Limited
  Navigation colours of `nav-plain-road-signs`) are declared in no base library. No base or modded
  single-player sector names them, so nothing spawns them.

## Uncertain

- Where the three new archetypes come from (an editor library, another mod) is not known.
  Sehlakalase's sign texture `sehlakalase_m.xbt` is redrawn with the others in
  `graphics-sign-textures`.
