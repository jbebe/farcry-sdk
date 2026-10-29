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

- **World 1:** `SubvertRewardW1` enables one layer of `Missions/SafeHouse/Upgrades/W1/` per subverted
  job, counted in `Globals.MASTER_GameGlobals.SH_W1Level`. The order is not numeric: `Level_01`,
  `02`, `06`, `03`, `05`, then `04`, which also disables `Level_01` (the armed vehicle replaces the
  plain one).
- **World 2:** `SubvertRewardW2` does the same with `Missions/SafeHouse/W2/`: `Level_04`, `01`, then
  `02` (disabling `Level_00`, on by default), `03`, `06`, then `05` (disabling `Level_02`). Only the
  vehicle tiers are ever disabled.

## What the difficulty setting changes

The setting (0-3) is stored in the save and read through `CGameplayManager::GetDifficultyLevel`. Its
44 readers in the server build, by what they change:

- **The player's health:** the maximum ([the player](./stims-and-damage.md#the-player)), how fast
  sickness drains it, and the last-bar cushion.
- **Healing:** syrette caps (6 / 5 / 4 / 3), health pickups. Medic stations take a different branch on
  Infamous; what it does was not checked.
- **Ammunition:** maximum ammo, clip pickups, grenade drops, reloading.
- **The player's weapons:** jam chance and wear per shot, upgrade bonuses (`CPawnBonusPlanManager`),
  the gamepad's aim-assist radius, and [point-blank damage](./stims-and-damage.md#point-blank-damage).
- **The AI:** forced misses and the squad role split ([where difficulty acts](./ai.md#where-difficulty-acts)).
- **Telemetry only:** mission start and stop events.

Nothing reads how the player plays - deaths, accuracy or time - so difficulty never adapts.
Progression and the act scale the enemy instead: their weapons (below), their optional tactics, their
weapon condition and how often they go down
([where difficulty acts](./ai.md#where-difficulty-acts)).

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

## What the pause menu's pages read

:::info[Verified via reverse engineering and in game]
Traced in the Steam `Dunia.dll` against the server build's symbols, and read by
`mods/inventory-overhaul` on retail GOG v1.03 in the demo range **(seen in game, 2026-10-27)**.
Addresses in this section are Steam.
:::

**Strings.** Every page words what it shows through `CStringTableMgr::Localize` (`0x104d1e40`,
`__thiscall` on `[0x11644778]`, `(const std::string* section, const std::string* key, u32 file)`,
`ret 0xC`), which returns the string as a `const wchar_t*`. It only reads the two strings' text, and
the file id every page passes is the constant `0x079C5159`, kept in several data copies
(`0x10f9d874`, `0x10f9dcc8`, `0x10f9ddc0`) that the address library does not map.

**Reputation and sickness.** The Journal (`PAUSE_REPUTATION`) reads two bytes of
`CFCXCountersComponentPlayerSP`: the reputation level at `+0x184` and the sickness level at `+0x68`,
each 0–5. Its text is `PauseMenu/REPUTATION_INFAMY_DESCRIPTION_<level>` and
`REPUTATION_SICKNESS_DESCRIPTION_<level>`, which from level 1 open with their own heading line.

**Jackal and Predecessor tapes.** `CFCXMissionManager` is a game mode service (class id
`0x11649ebc`, built by `0x10666680` while the name at `0x11649eac` is unset). Its Jackal tapes are at
`+0x158` and the Predecessor's (`PartnerTapes`) at `+0x164`, each a pointer and a count of
`0x78`-byte records: the string key at `+0`, found at `+0x74`. The keys are `A1JT00`…`A2JT16`, then
`W1V1M1`…`W2V3M1`, `C0V0M3` and `C0V0M4`. A tape's title is `JackalTapes/<key>`, its line
`<key>_DESC`; the Predecessor's lines have no recording date. Lua's
`GetFCXMissionManager():NewJackalTapeFound()` marks the next Jackal tape of the current world found
and plays it; in the demo range it found 09 to 13. `PartnerTapeFound("W1V1M1")` marks and plays a
Predecessor tape by key **(seen in game, 2026-10-28)**.

The record's other fields, as `SMissionTape::RegisterProperties` (`0x1074e4d0`) names them: the
description at `+0x1C`, `startSoundID` and `stopSoundID` at `+0x38` and `+0x54` (strings,
`"0x%08X"`), `soundType` at `+0x70`. The Bowa-Seko repeat fix patches the world test in
`NewJackalTapeFound` (`0x1074e464`), which reads a byte at `+0x75` **(RE-verified)**.

**Playing a tape** **(RE-verified; played from a plugin while the game runs, 2026-10-29)**. Every player of a tape, the pause pages and both Lua
calls, ends in `CJackalTapeManager::PlayTape` (`0x10717fa0`, `__thiscall(manager, const SMissionTape*)`,
`ret 4`), which needs nothing of the pause menu:

- **The manager** is `[[0x1164c788] + 0xC]`. The pause page (`PlaySelected`, `0x10857790`, the row's
  user data being its record) first calls `0x104dd4f0(manager)`.
- **PlayTape** calls `StopTape` (`0x10717e90`, `__thiscall(manager)`), finds its own entry by the
  record's key (`+0xE0` entries, `+0xE4` count, `0x2C` each; banks loaded at manager init), and plays
  the start id through the music manager's tape slot, `0x106fa7d0(id, type, -1.0f, 0)`. The slot stops
  the in-game music and starts the sound with `CSoundSystem` vtable `+0x94`, which unlike `+0x9C`
  ignores the type's inhibit flag, and sets the tape's type to 0 dB.
- **StopTape** plays the playing entry's stop id through the same slot. Nothing else in the pause
  menu stops a tape: leaving the page or the menu does not; a re-pick restarts it; a phone call
  (`0x106ea930`), a loading screen and scripted music on the other slot do.
- **Whether one plays:** the slot's handle `[0x1164c234]` is -1 when nothing does, reset by the music
  manager's update (`0x106fd830`) when the sound ends, and `[0x1164c22c]` holds the playing start id.
  The manager's own playing mark (`entry+0x28`) stays set after a tape ends. While scripted music holds
  the other slot, the slot drops a tape silently, so the handle is the test that it started.
- **Pause** applies the `Exclusive.In-Game Menu` mix and pauses `SoundPauseConfig`'s types, both lifted
  on resume, so a tape plays on in the game. The tapes have no subtitles.

**The current job.** The pause menu's main page (`CFCXPauseMenuPage::MissionObjective`, `0x10855d30`,
and `SetupMissionObjective`, `0x10856ac0`) walks the entity system's game elements and reads each one's
`CMapElementComponent` that is on (`+0x16C` non-zero) and has a state:

| Offset | Field |
|---|---|
| `+0x128` | `StateId`, a `std::string`: the job's line in `Objectives` |
| `+0x144` | `ObjectiveId`: the objective's line |
| `+0x16C` | on |

The first marker whose state is not the Underground's (`CGameDominoHelpers::IsUndergroundMission`,
`0x10722160`, `cdecl`) gives MISSION DETAILS, at most two such markers give OBJECTIVES (the one whose
key ends in `_01` first), and the Underground's marker gives its line. One caller runs four
`__thiscall(page)` methods in a row: `MissionObjective` (`PAUSE_INFODESCRIPTION_TEXT`),
`SetupMissionObjective` (`MISSION_OBJECTIF_LIST`), `0x108569a0`, which writes the Underground's line
into the widgets the page keeps at `+0x1B8`, `+0x1BC`, `+0x1C0` and `+0x1D8`, and `0x10855ba0`. They
show what they fill, so hiding the panel in `sp_pause_menus.mgb` is not enough: an Underground job
still showed with every element of it marked `HIDDEN` **(seen in game, 2026-10-29)**. The walk is
`GenericEntityMapIterator`: three words (the map's node, the entity's node, the map at the entity
system `[0x11644e80] + 0x1C`), started from the map head's first node by `0x1070ddb0` and stepped by
`0x1070e0d0`; an entity is `[[inner + 0x10] + 0xC]`, locked by `0x104dd4f0`, and its marker is
`GetComponent` (`0x104e2cc0`) with the class id at `0x10fde188`, built by `0x10041e60` while the name at
`0x10fde178` is unset. Texts go through `GetDescriptionWithBuddyName` (`0x10854750`), which fills in
`PRIMARY_BUDDY_NAME`, `RESCUE_BUDDY_NAME`, `MISSION_BUDDY_<id>`, `ASSIGNED_<id>`, `FIRST_LT`,
`SECOND_LT` and `WINNING_*` and reads nothing of its page. It takes a result pointer and the text as
an engine `std::wstring` by value (`ret 0x20`) and frees that text, so the text must be the engine's
own: the string table's lookup into a string, `0x104d1fc0` (`__thiscall(table, out, section, key,
file)`, `ret 0x10`), makes one; `0x10002890` destroys the result. In world 2's skytest save it read
"Thanks to my latest efforts, I can pretty much write my own ticket…" and the objective "Go to any
briefing location to get a mission." **(seen in game, 2026-10-28)**.

**The pause menu's entries.** `CFCXPauseMenuPage::Setup` (`0x10853540`) adds them one by one with
`CListMenuPage::AddButton` (`0x10cdbb80`, `__thiscall(page, const wchar_t* label, bool enabled,
handler)`, `ret 0xC`, returning the button's index): Resume, Journal (`PAUSE_REPUTATION`), Buddies,
Legend, Upgrades, Jackal Tapes, Predecessor Tapes when that content is unlocked, Statistics, Options,
Load Game, Save Game, Quit to Menu, Quit. It keeps the Jackal Tapes', Predecessor Tapes' and Save
Game's indices at `+0x1F0`, `+0x1F4` and `+0x1EC` to enable them later, which skips an index of -1.
An entry left out of `AddButton` with -1 returned is gone from the menu **(seen in game,
2026-10-28)**; as its handler is left out of the page's list with it, the others keep theirs
**(inferred)**.

**The pause menu's look.** Its main page is `PAUSE_MENU_PAGE`, `sp_pause_menus.mgb`'s `#C1849B53`.
The entries are the rows of a list panel it instances, `common.mgb`'s `#9EA91A65`, whose list uses
the same row button (`#1E789210`) as the game-over dialog. That dialog is `common.mgb`'s
`MESSAGEBOXLIST` page `#D24218AB`: a dimming fill, the clouds backdrop `#EB3BB25F`, a post-it area
`#D096B132` that pops in, and the list. Its strings are `MBOXLISTHEADER_GAMEOVER` and
`MBOXLISTGAMEOVER_*`. Moving the panel onto a copy of that post-it, with the notebook spread, its
paperclip, the glows and the buddies' portraits hidden, makes the pause menu look like the dialog
**(seen in game, 2026-10-28)**.

**Buddies.** `CBuddiesManager` is one too (class id `0x1164a484`, built by `0x10681c50` while the
name at `0x1164a474` is unset). Its `SBuddyData` records are at `+0x44`, their count at `+0x48`,
`0xC0` bytes each:

| Offset | Field |
|---|---|
| `+0x00` | name, a `std::string` such as `Frank_Bilders` |
| `+0x74` | gender |
| `+0x98` | unlocked |
| `+0x9C` | history points |
| `+0xA0` | life state, 0 alive |

`GetBuddyStatus` (`0x107463e0`, `__thiscall(manager, const std::string* name)`) gives 0 alive, 1
missing, 2 mercy killed, 3 rescue-ready or 4 wounded, and `CFCXUIBuddyUtil::GetBuddyStateString`
(`0x107575b0`, `cdecl(status, gender)`) words it from `PauseMenu/BUDDY_STATE_*`. The page's other
text is `Buddies/<NAME>_NATIONALITY`, `_AGE` and `_DESCRIPTION_1` to `_3`, and its portraits are
`sp_pause_menus.mgb`'s `buddies_*` materials.

From Lua, `GetBuddiesManager():BypassSetBuddyUnlock("Frank_Bilders", 1)` unlocks a buddy; passed
`true` instead of `1` it unlocked none **(seen in game)**. It skips the player's own character.
`CheatSetRescueBuddy(name)` makes a buddy the rescue buddy, and `SetBuddyRescueActive(1)` makes it
rescue-ready.

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
- How a buddy rescue runs once it is available. How the game chooses between a rescue, a scripted
  death and a game over is on [player death](./player-death.md).
