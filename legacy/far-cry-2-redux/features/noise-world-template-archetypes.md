---
title: Editor-template patrols and objectives added to the campaign library, unused
kind: noise
status: located
systems: [patrols, missions]
match:
  - "generated/entitylibrarypatchoverride.fcb/ghostpatrols/patrols/{fishingboat,fishingboat/m2_mounted,fishingboat/mk19_mounted}.xml"
  - "generated/entitylibrarypatchoverride.fcb/domino/{objectives/objectivepoint_todelete,objectives/pgp,objectives/randomencounterobjective_todelete,objectivestest/test1,objectivestest/test2}.xml"
exclude: []
requires: []
verified: diff
---

# Editor-template patrols and objectives added to the campaign library, unused

8 new archetypes in `generated/entitylibrarypatchoverride.fcb` that nothing in the game places or
names. All exist only in `worlds/tmpla`, the editor's template library, which the campaign does
not load; the mod's library appears to have been built from it, as Realism Plus's was
([`noise-world-template-archetypes`](../../realism-plus/features/noise-world-template-archetypes.md)).

- **Patrols** (3): `GhostPatrols.Patrols.FishingBoat`, `FishingBoat.M2_Mounted` and
  `FishingBoat.MK19_Mounted`. The mod recrewed them like its live patrols (`Blue_Faction.Assault_Nubian`
  with a `ShotgunMan_Nubian`; two `Red_Faction.Assault_Caucasian`; two `Blue_Faction.Assault_Caucasian`).
  No mapsdata patrol entity names them, and the two gun boats' vehicles
  (`Sea.FishingBoat.M2_Mounted`, `MK19_Mounted`) are not in the campaign libraries either.
- **Objectives** (5): `Objectives.ObjectivePoint_TODELETE`, `Objectives.PGP`,
  `Objectives.RandomEncounterObjective_TODELETE`, `ObjectivesTest.Test1`, `ObjectivesTest.Test2`.
  The sectors place `Objectives.PGP.PGP_Ammo`, `PGP_Fuel` and so on, the vanilla children, never
  the parent.

Checked: no sector, mapsdata entity or script in the base game or the mod names any of them.
