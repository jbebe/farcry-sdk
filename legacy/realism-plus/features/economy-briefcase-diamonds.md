---
title: Small diamond briefcases hold two
kind: component
bundle: gameplay
status: located
systems: [economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/oa_missionpickups/missionpickups/diamondbriefcase_lvl*.xml#**/nDiamonds"
exclude: []
requires: []
verified: diff
---

# Small diamond briefcases hold two

The smallest diamond briefcase gives two diamonds instead of one.

## How

`OA_MissionPickups.MissionPickups.DiamondBriefcase_LVL1`, the mod's copy in
`generated/entitylibrarypatchoverride.fcb`: `CPickupDiamond/nDiamonds` `1` -> `2`. The level 2 and 3
briefcases are not changed.

## Depends on

Nothing. Scubrah's Patch adds two to all three levels, in the world libraries
([`briefcase-diamonds`](../../scubrahs-patch/features/briefcase-diamonds.md)).
