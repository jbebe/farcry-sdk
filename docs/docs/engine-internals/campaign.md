---
sidebar_position: 17
---

# The Campaign — Missions, Reputation, Buddies and Malaria

The rules the campaign runs on, as the engine applies them. Everything here was read on the GOG 1.03
`Dunia.dll`, with class and function names from the Linux server's symbols, or measured in the retail
data **(RE-verified)**, unless a line says otherwise. None of it was tested in a running game.

Two related rules live elsewhere: how weapons [jam and wear
out](../modding/replacing-a-weapon.md#jamming-and-breaking), and how [mission records and layer
states travel with a save](../file-formats/savegame.md#campaign-state-outside-the-entities).

## How a mission's graph starts

Missions are [Domino graphs](./domino-scripts.md), and one master graph per world starts the rest.

**The master graph** is an omni entity in `<world>.omnis.fcb`. `DominoOmniEntity.MASTER_WORLD1_SCRIPT`
runs `Domino\User\MASTER_World1.World1.lua` at persist level 3, beside four DLC graphs,
`Dlc1World` to `Dlc4World`. World 2 has the same shape with `WORLD2_MASTER_SCRIPT`. Five sectors in
each world also carry four `DominoEntity_9` to `_12` entities in layer `main`, running
`Domino\User\DLC\Dlc1Weapons.graph.lua` to `Dlc4Weapons` and starting on load. `<world>.mapsdata.fcb`
holds no graph.

**A mission graph is spawned.** The master graph's `StartScript` boxes call
`CDominoManager:SpawnDominoEntity` (`0x105B6BF0`). That builds an omni entity named
`DominoOmniEntity_<script>` with a `CDominoComponent` on the file, at persist level 3, and reports
`Started` and `Closed` back through spawn and remove callbacks. A mission graph ends itself with
`CloseScript`, which removes its own entity. 44 of the 45 mission graphs do; the exception is
`a1bu00_tutorial`'s story-mission graph.

Story and buddy missions are started by `StartScript` boxes in the master graph directly. Library
missions go through one `BASEBRIEF_LIB-MISSION_LOADING` box each (`A1LM01`–`06` in world 1,
`A2LM07`–`12` in world 2):

1. Selecting the mission arms its box.
2. The HQ doorman then starts its briefing graph.
3. `LibraryMission_Accepted` with the matching `AcceptedMissionID` starts its mission graph.

**A running graph is saved with its state.** Each Domino omni entity is a `PersistenceDB` record
whose `CDominoComponent` carries the graph's `LuaState`. A spawned graph's record also keeps its
description, `fileBoxPath` included.

## Mission layers

A mission layer is a named group of world entities that a graph switches. `CBaseMission` keeps its
state at `+8`:

| Call | Effect |
|---|---|
| `Off` | state 0 |
| `Enable` | sets 1, clears 2 |
| `Disable` | only on an enabled layer: clears 1, sets 2 |
| `Complete`, `Fail` | 4, 8 |

Lua reaches `Enable`, `Disable`, `Off` and `IsEnabled` on `CBaseMission`, and graphs use them
through the `SetMissionState` and `GetMissionState` boxes. A world's `game.xml` lists every layer
with its default state: world 1 has 840, of which 41 start on, and world 2 has 804, with 44 on.

## Which missions are offered

`CFCXMissionManager::SelectMission` (`0x10748940`) counts the story missions done (S) and the
library missions done (L), over all acts:

- **Story missions** are offered while S < 2. They are also offered once all six act-1 library
  missions are done and 2 ≤ S < 8, and once every library mission is done.
- **Library missions** are offered at S = 2 while L < 6, and at S = 8 while 6 ≤ L < 12.
- **Buddy missions** take the library missions' place in three cases: there is no primary buddy,
  its history points are below `MinHistoryPointsForPrimaryBuddy`, or more than 3 missions have
  passed since the last buddy unlock.

`A2LM09`, `A3SM15` and `A3SM16` have special handling.

## Objectives and map markers

Markers are the 23 `Domino.Objectives.*` archetypes, each a `CGameElementEntity` with a
`CMapElementComponent`. The component carries:

- `StateId` and `ObjectiveId`
- `MarkerZ` and `CompassMarkerZ`
- `entEntityToFollow`
- `selState`: Disabled, Enabled or Available
- `selType`: Objective, PGP, SafeHouse, CellTower, WeaponShop, BusStation, Underground, HQ,
  MikesPlace, Other or City

The texts are in `oasisstrings`' `Objectives` section. A marker's `StateId` is the journal line and its
`ObjectiveId` the objective line: `A1AS01_01` is "Received a cellular message to terminate a
target.", `A1AS01_01_SAT_01` "Terminate the target on the map."

**One objective state is shown at a time.** `SetObjectiveState(state, showPopup)` (`0x10717420`) walks
every marker that has a `StateId` and the same underground class as the call. Markers whose `StateId`
equals `state` are enabled, and **every other one is disabled**. It then refreshes the road signs,
shows the popup if asked, and updates the progress tracker.

## Reputation from completed missions

`CFCXMissionManager::MissionCompleted` (`0x10745E50`) adds to the player's **base** reputation
(`SetBaseInfamyLevel`, `0x10698B70`). Some ids carry a fixed amount:

| Mission id | Reputation |
|---|---|
| `SAVE_BUDDY`, `RESCUE_REUBEN` | +5 |
| `HOUSE_CLEANING1_BASE`, `HOUSE_CLEANING2_BASE` | +10 |
| the `SUBVERT` ids, `BUDDY_BETRAYAL`, `KILL_WARLORDS` | +20 |
| `A1SM03` | set to 28, and the buddies of the defence reversal are marked as betrayed |
| `A2SM08` | set to 65 |

The rest depends on the kind of mission:

- **Library (faction) missions** give +3 while their `CompletionOrder` is below 4 and +4 after
  that, plus 1 outside act 1. A subverted one gives the same amount again.
- **Working for both sides** gives a one-time bonus of +4 in act 1 and +5 later. It is paid when an
  earlier library mission completed in the same act was for the other faction than the latest one.
- **Buddy missions** give +2, except `A1BU08` and `A2BU05`.
- **Counters only:** `GRIN` adds to the completed underground missions. Ids with `CV` or `AS` at
  positions 2–3 add to the convoy and assassination counts.

## Rewards

**Diamonds are paid when a story or library mission is accepted**, not when it is completed. In
`common_customboxes.missionacceptedbroadcast` (and its debug twin), the `StoryMission_Accepted` and
`LibraryMission_Accepted` broadcasts feed `GiveMissionReward` with the accepted mission id.
`CFCXMissionManager::GiveMissionReward` (`0x107435A0`) pays the record's `DiamondReward` through the
economy component.

**Assassinations** pay through the same call with `ASSW1`, `ASSW2` or `ASSBUDDY`. These are the config's
`AssassinationRewardWorld1`, `World2` and `Buddy`: 10, 15 and 20 diamonds. No shipped graph passes
`ASSBUDDY`, although world 2 has `Target_Buddy` and `Envelope_Buddy` layers.

**Safe-house upgrades** come from subverted missions:

- **World 1:** `SubvertRewardW1` enables `Missions/SafeHouse/Upgrades/W1/Level_01` to `06`, by
  `Globals.MASTER_GameGlobals.SH_W1Level`, and disables the level before.
- **World 2:** `SubvertRewardW2` does the same with `Missions/SafeHouse/W2/Level_00` to `06`.
  `Level_00` is on by default.

## Enemy loadouts follow campaign progress

`InventoryPackDifficultyLevel` decides which entries of an enemy's inventory pack it spawns with.
Every completed story or library mission raises it by 1. Buddy missions raise it only once: when the
first buddy mission of act 1 completes. The setter clamps it at 27.

On spawn, `CInventoryViewPawn::OnSpawn` hands the level to `CInventoryPack::EquipInventoryPack`. That
picks the weapon, ammo and gadget entries of the pawn's pack whose level is −1 or equals the current
level.

## Buddies

**History points.** An unlocked buddy starts with 3 history points (`UnlockBuddy`), or 5 when it was
unlocked by the first buddy mission completed in the act. That first unlock also unlocks two random
locked, living buddies of the act, at 3 points each.

**Who is primary, and who rescues you.** Before a story mission, `SelectPrimaryBuddies`
(`0x1073CEA0`) gathers the buddies that pass three tests:

- unlocked
- belonging to the current world (act 1 is world 1; acts 2 and 3 are world 2)
- alive

It sorts them by history points, highest first. Three rules follow:

- With two or more candidates, the first becomes the primary buddy.
- The second becomes the rescue buddy only when there are three or more. It keeps the previous
  rescue buddy's active state.
- With a single candidate, it becomes the mission's buddy only during a story mission.

So a rescue needs three living, unlocked buddies in the world.

A rescue is available (`IsRescueBuddyAvailable`, `0x10735DA0`) when a rescue buddy is already
spawned, or when the rescue record exists and is active. Scripts can switch rescues off and on with
the Lua calls `BlockBuddyRescue` and `UnblockBuddyRescue`. They set `CBuddyRescue`'s
`DisabledByScripts`, which a save keeps.

## Malaria

`gamemodesconfig.xml`'s `<Malaria>` block names four curves (`FirstAttackTime`,
`BetweenAttackTime`, `MinorAttackQte`, `MinorAttackDuration`), and the launcher scales the three time
curves; see the [function registry](./function-registry.md). The rest is
`CFCXCountersComponentPlayerSP`:

- **Pills.** `SetMalariaPillCount` clamps the count to 0–4, and taking a pill only decrements it
  while the count is below 4. A count of 4 is unlimited pills.
- **Minor or major.** The first attack is always minor. After that an attack is major once the
  attack count reaches `MinorAttackQte`. From act 3 a major attack is downgraded to minor.
- **No attack in a desert zone.** `CanTriggerMinorAttack` refuses any attack while the player is in
  a desert zone. A major attack there falls back to minor, which is refused as well, so the attack
  waits until the player leaves the zone. Health failure blocks both kinds.
- **A pause after dialogue.** When someone talks, attacks are frozen for 60 s (`0x1069A9B0`).
- **Vehicles.** A major attack or a blackout first takes the player out of a vehicle or off a
  mounted weapon (`0x10699600`). A minor attack does not.

## Liberating a safe house

Each safe house keeps a list of the mercenaries guarding it. `RemoveMercFromSafeHouseCheckList`
removes one, and unlocks the house when the list becomes empty. It runs in three places:

- **A guard's death** (`CPawnAgent::CleanUpOnDie`): can unlock the house.
- **A guard's despawn** (`CAIWorld::RemoveExtraAgents`): can unlock the house too.
- **Loading a save** (`PostLoad`): removes the guard without unlocking.

`UnlockSafeHouse` (`0x1068B480`) does four things:

- clears `bLocked` and sets `Discovered`
- unlocks the door
- puts the map marker in state 2 with `SafeHouse_Unlocked_GPS`
- **disables** the safe house's mission layer, switching off what that layer placed

## Unknowns

- Every rule here is read from code and data; none has been observed in a running game.
- Whether the one-time reputation bonus for working for both factions is ever re-armed.
- How a buddy rescue runs once it is available: the rescue manager's state machine was not traced.
