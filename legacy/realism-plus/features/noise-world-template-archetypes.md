---
title: Editor-template archetypes added to the campaign library, unused
kind: noise
status: located
systems: [ai, buddies, vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{merctest/*,missions/missionbriefingtest,blue_faction/carlgustaf_caucasian,red_faction/carlgustaf_caucasian,red_faction/carlgustaf_nubian,red_faction/mortarman_caucasian,red_faction/mortarman_nubian,red_faction/sniper_caucasian}.xml"
  - "generated/entitylibrarypatchoverride.fcb/buddies/**"
  - "generated/entitylibrarypatchoverride.fcb/vehicle/{air/paraglider/paraglider_lv1,air/paraglider/paraglider_lv2,air/paraglider/paraglider_lv4,land/bigtruck/tanker,land/bigtruck_tanker,land/rover/brokenrover,land/rover/stuckrover,lookcurves/lookpov_leftright,lookcurves/lookpov_updown,sea/fishingboat/m2_mounted,sea/fishingboat/mk19_mounted}.xml"
exclude: []
requires: []
verified: diff
---

# Editor-template archetypes added to the campaign library, unused

33 new archetypes in `generated/entitylibrarypatchoverride.fcb` that nothing in the game places,
spawns or names. All but one exist only in `worlds/tmpla`, the editor's template library, which the
campaign does not load; the mod's library appears to have been built from it.

- **Soldiers** (17): `MercTest.AimTest`, `Generic`, `GraphicKit_TEST`, `HealthTest`, `LeanTest`,
  `MovementTest`, `PawnLookat`, `PlayGesture`, `StoopidFollower`, `Target_Practice` and
  `Missions.MissionBriefingTest` (test characters); `Blue_Faction.CarlGustaf_Caucasian`;
  `Red_Faction.CarlGustaf_Caucasian`, `MortarMan_Caucasian`, `MortarMan_Nubian`, `Sniper_Caucasian`;
  and `Red_Faction.CarlGustaf_Nubian`, the one not in the template (the mod's own). Several carry the
  mod's other edits (the Nubian look, `0.6`/`3` sight multipliers).
- **Characters** (5): `Buddies.Wound`, `Buddies.Biped`, `MissionSpecific.SoF_Generic`,
  `Prison_Physician`, `PoliceOfficer`.
- **Vehicles** (11): `Air.Paraglider.Paraglider_Lv1`, `Lv2`, `Lv4`; `Land.BigTruck.Tanker`,
  `Land.BigTruck_Tanker`; `Land.Rover.BrokenRover`, `StuckRover`; `LookCurves.LookPOV_leftright`,
  `LookPOV_updown`; `Sea.FishingBoat.M2_Mounted`, `MK19_Mounted`. They carry the mod's vehicle values
  (faster engines, lighter tanker, glider fixes).

Checked: no sector entity, patrol, reinforcement list, script or inventory pack in the mod or the
base game names any of them, and none of the names is a string in `Dunia.dll`. They cost a little
memory and do nothing in game; a mod that wanted to place one would find it ready.
