---
title: Armory weapons not unlocked after the prison escape
kind: component
bundle: balancing
claims:
  - "Armory weapons are no longer unlocked automatically after escaping the prison in Bowa-Seko"
status: located
systems: [economy, weapons, missions]
match:
  - "domino/user/master_world2.world2.lua@L2131"
exclude: []
requires: []
verified: diff
---

# Armory weapons not unlocked after the prison escape

Arriving in Bowa-Seko no longer unlocks every armory weapon of the second world at once; they keep
being unlocked one convoy mission at a time, as in the first world.

## How

In `domino/user/master_world2.world2.lua`, `f_177_Out` runs when the prison mission script
(`A2SM09_Prison.A2SM09_Mission.lua`, started by box 176) closes. Vanilla does three things there,
and the mod comments out all three:

- `self[217]._type.UnlockAll_W2(...)` - box 217 is `ConvoyMissions.UnlockWeapons.lua`, whose
  `UnlockAll_W2` shows the `TU70B_MESSAGE` weapon-shop popup and unlocks the world-2 armory items.
- `en_21` (sets `World = 2` on box 21) and `self[21]._type.Stop(...)` - box 21 is
  `ConvoyMissions.Convoy_Missions.lua`, so vanilla also stops the world-2 convoy mission chain here.

## Uncertain

- Keeping the convoy chain running is what lets the remaining weapons still be unlocked by convoy
  missions; that the two lines go together for that reason is an inference from the vanilla script.
