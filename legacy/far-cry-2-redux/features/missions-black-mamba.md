---
title: Black Mamba buddy rescue replaces the Dogon Sediko rescue
kind: component
bundle: missions
claims:
  - "The south map Sediko rescue mission has been REPLACED with the \"Black Mamba\" buddy rescue mission due to frequent bugs resulting from the Sediko mission."
  - "Added the Black Mamba buddy rescue mission. This mission now replaces the Dogon Village rescue in ACT 2."
status: located
systems: [missions, buddies]
match:
  - "domino/user/master_world2.world2.lua@*"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/MissionManagement/BuddyMission/Item[BlackMamba]"
exclude: []
requires: []
verified: diff
---

# Black Mamba buddy rescue replaces the Dogon Sediko rescue

In act 2 the buddy rescue that would free a captive from the Dogon Sediko is swapped for the cut
"Black Mamba" rescue (`A2BU05`), whose script ships unused in the retail `common` archive: a downed
buddy the player heals and wins over.

## Which mission is replaced

Both readme lines describe the same mission. The data replaces `A2BU07` (`DogonRing`), whose
objective reads "Free the captive from the locked structure in the Dogon Sediko" (`A2BU07_01_UBM_01`)
- the "Sediko rescue" of the notice and the "Dogon Village rescue" of the changelog. The other act 2
rescue, `A2BU06` (`OldBrewery`), is untouched.

## How

- **`domino/user/master_world2.world2.lua`**, the world 2 master graph, 7 hunks, each turning an
  `A2BU07` reference into `A2BU05`:
  - `@L36`: the graph's script list names `Domino/User/A2BU05_Blackmamba.A2BU05_Mission.lua` in
    place of the Dogon Ring script.
  - `@L1753`: the `StartScript` box `150` starts `Domino/User/a2bu05_blackmamba.a2bu05_mission.lua`
    instead of `A2BU07_DogonRing.A2BU07_Mission.lua`; `@L119` and `@L3035` rename the graph variable
    that holds the running script.
  - `@L728`: the `ConsoleCommand` box `64` that fires that start listens for `A2BU05` instead of
    `A2BU07`.
  - `@L1278` and `@L1296`: on the `ShutDownBuddies` message the graph stops the `A2BU05` script and
    removes `Globals.MASTER_GameGlobals.A2BU05_BuddyID` instead of `A2BU07_BuddyID`.
- **`engine/gamemodes/gamemodesconfig.xml`**, `MissionManagerService/MissionManagement/BuddyMission`:
  a live `Item act="2" id="A2BU05" name="BlackMamba" faction="Blue" achievement="buddyunlocked"`.
  Vanilla carries it commented out ("discover to unlock"); the `A2BU07` item stays. Registration
  matters: `CFCXMissionManager::ActivateBuddyMission` (traced in `FarCry2_server`) marks a
  registered buddy mission active, and an unregistered code is only stored as the act's teleport
  buddy mission.

World 2 already carries the mission: `world2.game.xml` has `Missions/BuddyUnlockMissions/A2BU05`
with its `A2BU05`, `A2BU05_BuddyObj` and `A2BU05_Medecine` layers, and `world2.mapsdata.fcb` has
`SpawnPointBuddy_A2BU05_1`. The mod adds no world data for it. The script finds the buddy downed,
plays the healing animations, then runs `AssignBuddy` and `MissionCompleteBroadcast` for `A2BU05`.

This is exactly the recipe of Boggalog's guide, which credits Hunter for choosing the Dogon Ring
mission ([misc: Black Mamba](../../../docs/docs/modding/guide/misc.md#guide---using-the-black-mamba-buddy-rescue-mission)):
two file references and five mission-code references. The guide does not mention the
`gamemodesconfig.xml` item. `legacy/editor-with-ai`'s
[`gameplay-restored-missions`](../../editor-with-ai/features/gameplay-restored-missions.md)
registers the same item but never starts the script.

## Uncertain

- `A2BU05_BuddyID` is set by nothing: the Black Mamba script assigns no buddy-id global and
  `master_gameglobals.globals.lua` declares none, so on `ShutDownBuddies` the graph's `RemoveBuddy`
  receives nil and the rescued buddy is not removed by this path. What `ShutDownBuddies` is sent
  for is not traced.
- The HQ doorman graph (`master_world2.world2_hqdoorman.lua`, unchanged) still offers the rescue by
  the code `A2BU07`, with the Dogon Sediko objective text, and has no branch for `A2BU05`. How the
  master graph's start box is reached in normal play (its only caller is the console-command box)
  is not traced, so whether the player is still sent to the Dogon Sediko first is open. The
  guide's video shows the swapped mission playing.
- `A2BU07` stays registered; what the mission manager does with a registered buddy mission whose
  script never runs is not traced.
