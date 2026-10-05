---
title: Convoy missions offered until all are done
kind: component
bundle: fixes
claims:
  - "Fixed an issue where convoy missions could stop being offered before the player could complete all of them"
status: located
systems: [missions]
match:
  - "domino/user/sidemissions/convoymissions.convoy_missions.lua@L857"
  - "domino/user/sidemissions/convoymissions.unlockweapons.lua@L303"
exclude: []
requires: []
verified: diff
---

# Convoy missions offered until all are done

The arms merchant keeps offering convoy missions until the player has taken all four of a world's,
instead of stopping as soon as the chain reaches its last entry.

## How

- `domino/user/sidemissions/convoymissions.convoy_missions.lua@L857` ("Update 2.7"): in
  `f_70_Out_1`, vanilla turned the `Missions/ConvoyMissions/ArmsMerchant` mission off when
  `IsLastConvoyMission` was true and enabled the next convoy otherwise. Now the outcome ignores
  `IsLastConvoyMission`: in world1 (detected by `Missions/StoryMissions/A1SM01/A1SM01_StreetFighting`
  existing) the merchant is turned off only once `Globals.MASTER_GameGlobals.ConvoyMissionsAccepted_W1`
  is at least `4`; in world2 only once `ConvoyMissionsAccepted_W2` is at least `4` and
  `FinalConvoyMissionCompleted` is `1` ("Update 3.2: Secondary fix for the same bug").
- `domino/user/sidemissions/convoymissions.unlockweapons.lua@L303` ("Update 3.2"): the fourth
  unlock step (`f_11_Output_3`) sets `FinalConvoyMissionCompleted = 1`.

## Depends on

Parts on other pages:

- `ConvoyMissionsAccepted_W1`/`_W2` are counted up in
  `domino/user/common_customboxes.missionacceptedbroadcast.lua@L86`, only for a mission tagged
  `MissionType = "CM"`; the convoy script gets that tag at `convoymissions.convoy_missions.lua@L374`
  (with `SideMissionType = "CM"` at `@L442` and `@L685`). Those are the Concurrent Missions
  bookkeeping.
- The three globals are declared in `domino/user/master_gameglobals.globals.lua@L90`.

Without the counter, `ConvoyMissionsAccepted_*` never reaches 4 and the merchant never stops
offering.
