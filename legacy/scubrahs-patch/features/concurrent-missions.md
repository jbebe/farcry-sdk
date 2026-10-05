---
title: Concurrent missions
kind: component
bundle: features
claims:
  - "Concurrent Missions: Allows you to complete all mission types simultaneously"
status: located
systems: [missions, buddies, ui]
match:
  - "domino/system/objectivestate.lua@L32"
  - "domino/system/gameelementobjective.lua@L48"
  - "domino/user/common_customboxes.missionacceptedbroadcast.lua@L{86,130,131,191,192,292}"
  - "domino/user/common_customboxes.missioncompletebroadcast.lua@L{42,65,75,85,279}"
  - "domino/user/common_missionbriefings.scriptedpawn_interact_plus.lua@L903"
  - "domino/user/common_buddysidequests.bsq_activate_and_cancel.lua@L*"
  - "domino/user/common_buddysidequests.bsq_missiondebriefing.lua@L{69,78,84}"
  - "domino/user/sidemissions/assassination_missions.*.lua@L*"
  - "domino/user/sidemissions/convoymissions.convoy_missions.lua@L{148,160,374,442,685}"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w1.lua@L*"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w2.lua@L*"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmission_and_pawnbrief_selection.lua@L{209,661,786,1224,1452}"
  - "domino/user/sidemissions/sidemissions.mikesplace_foreachbuddy_spawned.lua@L*"
  - "domino/user/savepoints/savepoints.savepoints.lua@L{848,1033,1063,1125,1195,1241,1271,1301,1347,1377,1573,1619}"
  - "domino/user/master_world1.world1.lua@L{955,1150,1186,1192,1194,1239,2987,3258,3259}"
  - "domino/user/master_world2.world2.lua@L{1145,1583,1585,1591,1593,1627,2945,2953}"
  - "worlds/*/generated/world*.mapsdata.fcb/{objectives.*,objective_assassination_*,a1sm03_mikesdefensemarker_*}.xml#**"
  - 'worlds/*/generated/world*.mapsdata.fcb/_layout.xml#layer[missions\{buddysidequests,assassinationmissions,convoymissions}\*]{,/**}'
  - "install/bin/dunia.dll@0x855a69"
  - "install/bin/dunia.dll@0xeb0cec"
exclude:
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w1.lua@L669"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmanager_w2.lua@L603"
requires: [missions-game-globals, patrols-set-mission-state, missions-mission-completed]
verified: re
---

# Concurrent missions

In vanilla, accepting a mission cancels whatever else is running. With the mod a story or faction
(library) mission, a buddy side quest (BSQ), a convoy mission and an assassination can all be active
at once, each keeps its map markers, and each completes with its own "mission concluded" message.

## How

The mod tracks each mission type in its own global and stops the cancellation paths from firing.
Everything is in Domino scripts plus marker entities in `mapsdata`.

**Which mission is active, per type.**
`common_customboxes.missionacceptedbroadcast.lua`: `@L86` reads a new `MissionType` (`AM`
assassination, `CM` convoy, `BM` buddy quest) and stores the id in `AcceptedAssassinationMissionID`,
`AcceptedConvoyMissionID` or `AcceptedBuddyMissionID` (plus `BSQBuddyName`, `BSQObjectiveState`),
pushes a contextual `MISSION_ACCEPTED` HUD message (`CellTower`, `WeaponShop`, `MikesPlace`) and counts
accepted convoys per world (`ConvoyMissionsAccepted_W1`/`_W2`, read by `convoy-offer-fix`); `@L130`,
`@L131`, `@L191`, `@L192`, `@L292` set `PrimaryMissionActive` and `AcceptedStoryMissionID`,
`AcceptedLibMissionID`, `AcceptedSideMissionID`. The senders are tagged: `MissionType = "AM"` /
`SideMissionType = "AM"` in `assassination_missions.assassination_missions.lua@L449`, `@L684`,
`@L822`, `@L835`; `"CM"` in `convoymissions.convoy_missions.lua@L374`, `@L442`, `@L685`; `"BM"` in
`mikesplace_bsqmission_and_pawnbrief_selection.lua@L1224`, `@L1452`.
`common_customboxes.missioncompletebroadcast.lua` `@L42`, `@L65`, `@L75`, `@L85` tag completions as
`Story`/`Lib`/`Side` and clear `PrimaryMissionActive`; `@L279` adds an `f_17_Out_0` that hands
`MissionCompleted` the id stored for the completed side mission's type instead of the single vanilla
`AcceptedMissionID`.

**No cancellation.**
- `scriptedpawn_interact_plus.lua@L903` and `assassination_missions.assassinationcelltowerinteracted.lua@L401`,
  `@L504`: talking to a mission giver or a cell tower while a mission is active no longer opens the
  `SQ_CANCEL` "this cancels your mission" box; both comparison branches go on to the briefing.
- `assassination_missions.assassination_missions.lua@L196`, `@L223` and
  `convoymissions.convoy_missions.lua@L148`, `@L160`: an assassination or convoy that is already active
  is not initialised again.
- `common_buddysidequests.bsq_activate_and_cancel.lua`: library and side mission acceptance and side
  mission completion no longer cancel the buddy quest (`@L45`, `@L51`); the listener stays on (`@L96`)
  and the quest is cancelled only when the final world 1 story mission `A1SM02` is accepted (`@L147`),
  so no world 1 quest is still open in world 2.
- `bsq_missiondebriefing.lua@L69`, `@L78`, `@L84`: the debriefing's pawn boxes ignore `Canceled`.
- `mikesplace_bsqmanager_w1.lua`, `_w2.lua` (all hunks but `w1@L669`/`w2@L603`): Mike's bar no longer
  reacts to library/story acceptance or library completion (only story acceptance in world 1 cancels
  quests, for the same `A1SM02` reason), no longer cancels secondary buddies' pawn briefings, does not
  despawn buddies while a quest is active (`AcceptedBuddyMissionID` `"x"` means none), regenerates the
  bar's buddies after library/side completions (new `RegenerateBuddies`) and keeps the current library
  mission's buddy out of the regeneration (`en_63_PrimaryCheck` / `en_62_PrimaryCheck`, by
  `GetPrimaryBuddyName()`). `w2@L1689` re-enables Mike's door (`CDoor_Enable`) instead of locking it
  while `A3SM13_Reuben` runs.
- `mikesplace_bsqmission_and_pawnbrief_selection.lua@L209`, `@L661`, `@L786` and
  `mikesplace_foreachbuddy_spawned.lua@L335`, `@L339`: pawn-briefing cancellation and the primary
  buddy's spawn at Mike's are skipped while a quest or a library mission is active.
- `savepoints.savepoints.lua`, the twelve `CompareStrings` hunks: safe-house buddy placement now tests
  `AcceptedLibMissionID` instead of `AcceptedMissionID`, which side missions overwrite.

**Markers and messages.**
- `domino/system/objectivestate.lua@L32`: the `ObjectiveState` box records the quest's step in
  `BSQObjectiveState`, enables the right marker entity for every assassination (`A?AS0?_01`), convoy
  (`A?CV0?_01`) and quest step (`BSQ??_0?`), turns the generic library "mission completed" state
  (`A1LM00_00`, `A2LM00_00`) into a sound and `MISSION_CONCLUDED` message while a primary mission is
  active, and after any real `SetObjectiveState` (which resets map markers) re-enables the markers of
  every active assassination, convoy and quest.
- `gameelementobjective.lua@L48`: the arms dealers' "mission available" icons stay off while a convoy
  runs.
- `master_world1.world1.lua` and `master_world2.world2.lua`: faction "mission ready" icons are enabled
  only while `PrimaryMissionActive` is `1` (`w1@L955`, `w2@L1145`); after a side mission the latest
  faction mission is no longer re-selected, after a story mission it still is (`f_48_Out_1` /
  `f_98_Out_1` split into `_Story` variants: `w1@L1186`, `@L1192`, `@L1194`, `@L3259`;
  `w2@L1585`, `@L1591`, `@L1593`, `@L2945`); and the generic library "mission completed" objective
  state is skipped (`_NoObjectiveState` variants: `w1@L1150`, `@L1239`, `@L2987`, `@L3258`;
  `w2@L1583`, `@L1627`, `@L2953`).
- `world1/2.mapsdata.fcb`: the side missions' marker entities (`Objectives.Missonobjective_*`,
  `Objectives.MissionObjective_*`, `Objective_Assassination_1/2`, the BSQ11 step-4 marker that vanilla
  names `A1SM03_MikesDefenseMarker_10`) change `tplCreatureType` from
  `Domino.Objectives.MissionObjective` to `Domino.Objectives.GRINObjective`, and most gain a
  `CMissionComponent` putting them in their mission's layer; the `_layout.xml` entries for
  `missions\buddysidequests\*`, `missions\assassinationmissions\*` and `missions\convoymissions\*`
  list them there.

## Depends on

- `missions-game-globals` declares every tracking field.
- `patrols-set-mission-state` holds the `SetMissionState` box changes (buddy quests surviving
  `Off`, arms dealer guard, `PrimaryMissionActive` for missions that start without acceptance). The
  same box's `DisableConvoyPatrols()` calls `Disable()` on ghost-patrol layers without nil checks, so
  enabling a convoy is expected to error unless `randomized-patrols-core` is picked too (inference).
- `missions-mission-completed` claims `domino/system/missioncompleted.lua@L32`, the hunk that also resets
  `PrimaryMissionActive` after the buddy unlock missions (`A1BU01`-`A1BU04`, `A2BU06`, `A2BU07`) and
  shows the contextual convoy and assassination "mission concluded" messages.

## Dunia.dll

The pause menu's objective list picks each objective's icon (`i_Obj_icon` in
`ui/sp_pause_menus.mgb`) from a three-letter code inside the objective id, e.g. `BSQ26_02_NBM_01`
(`FUN_10855850`): `*AT`/`*SV` get `icon_subvert`; `ABM`, `BBM`, `CAT`, `MBM`, `NBM`, `SAT`, `UBM`
get `icon_objective`; `*BG` get `icon_active`; `GBM` gets `icon_grin`; anything else is left blank.
Two patches make unmatched codes take `icon_grin` (the `jnz` to the blank case is NOPed) and blank
the codes `SAT`, `NBM` and `MBM` in `.rdata` (to `XAT`, `XXM`, `XBM`), so those objectives, all the
buddy side quests' `NBM` steps among them, show the GRIN icon too. Placed here because it matches the
side-mission markers turning into `GRINObjective`; that it was written for this is inferred.

| | Steam | GOG | Bytes |
|---|---|---|---|
| unmatched code | `0x10855A69` | `0x10848E39` | `75 12 -> 90 90` |
| code strings | `0x10EB0CEC` | `0x10E28874` | `SAT\0NBM\0M` -> `XAT\0XXM\0X` |

Patterns (one match in each build; sites at +15 and +28):
- `68 ?? ?? ?? ?? 56 FF D7 83 C4 08 85 C0 8B CB 75 12 68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 5F 5E 8B C3 5B C2 08 00`
- `47 42 4D 00 55 42 47 00 4E 42 47 00 4D 42 47 00 47 42 47 00 41 42 47 00 55 42 4D 00 53 41 54 00 4E 42 4D 00 4D 42 4D 00 43 41 54 00`

## Uncertain

- Why the markers become `GRINObjective` is inferred: `SetObjectiveState` resets the mission
  objective markers, and a separate class plus the explicit re-enables keep side-mission markers up.
- Several hunks only make sense together with others on this page; picking parts of it is untested.
