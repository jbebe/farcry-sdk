---
title: More diamonds in briefcases
kind: component
bundle: balancing
claims:
  - "Increased the amount of diamonds found in briefcases"
status: located
systems: [economy]
match:
  - "**/entitylibrary*.fcb/oa_missionpickups/missionpickups/diamondbriefcase_lvl*.xml#**/nDiamonds"
exclude: []
requires: []
verified: diff
---

# More diamonds in briefcases

Each diamond briefcase in the world holds two more diamonds.

## How

`CPickupDiamond/nDiamonds` on the three briefcase archetypes
`OA_MissionPickups.MissionPickups.DiamondBriefcase_LVL1/2/3`, in both `worlds/world1` and
`worlds/world2` entity libraries: `1 -> 3`, `2 -> 4`, `3 -> 5` (6 changes). The tutorial briefcase
(`DiamondBriefcase_TUTORIAL_ONLY`) and the editor template library `worlds/tmpla` are left alone.

## Depends on

Nothing. `economy-briefcase-tracker` adds the same 3/4/5 to the mod's script-side diamond count when
a briefcase is picked up, so the two must be picked together if that count is to stay right.
