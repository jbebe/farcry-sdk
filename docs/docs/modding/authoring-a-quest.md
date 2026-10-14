---
sidebar_position: 8.6
---

# Authoring a multi-stage quest

A quest an NPC gives in person: you walk up, they talk you into a job, you go and do it, you come
back, they talk again, and then they have the next job. Far Cry 2's buddy side quests (`BSQ01`–`BSQ26`)
work this way. This page takes one apart, Flora Guillen's `BSQ03` followed by her `BSQ15`, and gives
a recipe for a new one.

Read [authoring a mission](./authoring-a-mission.md) first: it covers the building blocks this page
uses, such as objective states, map markers, mission layers and the `AcceptedMissionID` global.
Claims are marked the same way: **(proven)**, **(RE-verified)**, **(seen in data)**, **(inferred)**,
**(untested)**. Nothing on this page has been run in game. What each quest says and pays, as a
player meets it, is in [side missions](../lore/missions/side-missions.md#buddy-side-quests-bsq01bsq26).

## The shape of a quest

A buddy runs through five stages **(seen in data)**:

| Stage | Where the player is | What happens | Bark tag |
|---|---|---|---|
| 1. Offer A | at the bar | the buddy beckons, greets and pitches the first job; the player accepts | `A1BSQA_SE01` |
| — | out in the world | the job graph runs; its last step points the player back to the bar | |
| 2. Debrief A | back at the bar | the buddy asks how it went, and the quest closes | `A1BSQA_SE02` |
| 3. Offer B | at the bar | the same buddy pitches the second job | `A1BSQB_SE01` |
| 4. Debrief B | back at the bar | the second debrief | `A1BSQB_SE02` |
| 5. Nothing | at the bar | small talk only | |

World 2 uses `A2BSQA_SE01` and the rest the same way.

## Who runs what

Five layers of graph, from the master graph down **(seen in data)**:

```
MASTER_World1.World1                        box 250
 └ SideMissions.MikesPlace_BSQmanager_W1    one per world: the bar, all its buddies, all their jobs
    ├ …BSQmission_and_Pawnbrief_Selection   one per buddy (box 34 is Flora): the stage machine
    │   ├ Common_BuddySideQuests.BSQ_MissionBriefing     stages 1 and 3
    │   ├ Common_BuddySideQuests.BSQ_MissionDebriefing   stages 2 and 4
    │   └ Common_BuddySideQuests.BSQ_NoMission           stage 5
    ├ Common_BuddySideQuests.BSQ_Activate_and_Cancel    one per job: starts it, cancels it
    └ BSQ03_HiddenArmory.BSQ03_Mission       one per job: the job itself
```

The manager hands each buddy's selection box its data **(seen in data)**. Flora's is:

```lua
self[34].World_Number   = 1;
self[34].BuddyPawn      = self.Entity_FloraGuillen;
self[34].StanceAnim     = self.Anim_FloraGuillen;
self[34].DoBeckon       = self.DoBeckon_FloraGuillen;
self[34].BuddyName      = "Flora_Guillen";
self[34].BSQA_MissionID = "BSQ03";
self[34].BSQB_MissionID = "BSQ15";
```

**"Then another mission" is just the pair `BSQA_MissionID` / `BSQB_MissionID`.** The selection box
remembers whether A and B are done in `BSQA_MissionCompleted` and `BSQB_MissionCompleted`, and
offers B only once A is done.

### The stage machine

The selection box picks its stage from three globals and its own two flags **(seen in data)**:

| Condition | Stage |
|---|---|
| no buddy pawn loaded | none |
| a side quest is accepted, `BSQ_MainObjectiveComplete` is true, and `AcceptedMissionID` is this buddy's A | 2, debrief A |
| the same, with this buddy's B | 4, debrief B |
| no side quest accepted, the bar is in offering mode (`W0_MikesPlace_BSQbriefings`), A not done | 1, offer A |
| the same, A done and B not done | 3, offer B |
| otherwise | 5, nothing |

`BSQ_MainObjectiveComplete` is the hinge between "go do it" and "come back". The job graph does not
set it: the manager sets it when the job graph fires `Succeeded`, then asks every buddy to pick its
stage again.

### What a job graph must provide

Ubisoft left three templates: `Common_BuddySideQuests.BSQ_Template_Destroy`, `_Kill` and `_Fetch`.
Every shipped job graph has their interface **(seen in data)**:

| Pin | Direction | Meaning |
|---|---|---|
| `In` | control in | the player accepted; switch the world on and show the first objective |
| `Cancel` | control in | the player took another job; switch the world off |
| `Succeeded` | control out | the job is done; the player should go back |
| `Finished` | control out | the graph has cleaned up after a cancel |

`BSQ_Activate_and_Cancel` calls `In` as soon as the briefing reports the job accepted. It calls
`Cancel` when a story, library or other side mission is accepted afterwards.

## Worked example: Flora's `BSQ03`, then `BSQ15`

### The files

| File | What it holds |
|---|---|
| `domino\user\sidemissions\sidemissions.mikesplace_bsqmanager_w1.lua` | the manager: Flora's data, `BSQ03` (box 71) and `BSQ15` (box 72), the `forcebsq` test command |
| `…sidemissions.mikesplace_bsqmission_and_pawnbrief_selection.lua` | the stage machine |
| `domino\user\common_buddysidequests.bsq_missionbriefing.lua`, `…bsq_missiondebriefing.lua` | the two conversations |
| `domino\user\common_missionbriefings.scriptedpawn_*.lua` | the conversation steps: beckon, greet, talk, accept, leave |
| `domino\user\common_buddysidequests.bsq_activate_and_cancel.lua` | start and cancel |
| `domino\user\bsq03_hiddenarmory.bsq03_mission.lua` | the job |
| `worlds\world1\generated\world1.game.xml` | the layer `Missions/BuddySideQuests/BSQ03`, off by default |
| `levels\w1_b_2\generated\worldsectors\worldsector4349.data.fcb` | the layer's one entity: `PickupPiles.AmmoPile_21`, id `2053359086612781735`, archetype `pile_archetypes.PickupPiles.AmmoPile` |
| `worlds\world1\generated\world1.mapsdata.fcb` | two markers, both in `main`: `Objectives.MissonObjective_5` at the ranch (1883, 3509), `A1SM03_MikesDefenseMarker_4` at Mike's (2612, 2131) |
| `languages\english\oasisstrings.rml` | `Objectives/BSQ03_01`, `BSQ03_01_NBM_01`, `BSQ03_02`, `BSQ03_02_NBM_01`; the `Subtitles` below |
| `scripts\game\barkdata\1431380.bank` and `.pfe`, `…\loc\1431380.spk` | Flora's lines for offer A |
| `…\1431385.*`, `1431390.*`, `1431395.*` | debrief A, offer B, debrief B |
| `engine\gamemodes\gamemodesconfig.xml` | the record `<Item act="1" id="BSQ03" name="Hidden_armory" infbonus="1" />` |

All **(seen in data)**.

### The markers

`BSQ03`'s two markers stay in the always-on `main` layer, and only `ObjectiveState` decides which one
shows **(seen in data)**:

| Marker | `StateId` | `ObjectiveId` | Text |
|---|---|---|---|
| at the ranch | `BSQ03_01` | `BSQ03_01_NBM_01` | "Destroy the ammo pile inside the Ranch House." |
| at Mike's | `BSQ03_02` | `BSQ03_02_NBM_01` | "Meet Flora Guillen at Mike's to debrief." |

Since setting one state hides every other marker, a multi-stage quest is just one marker per stage
and one `ObjectiveState` call per step. The return marker at Mike's is shared: `BSQ15_02` uses the
same spot.

### The conversation

A bark bank's number is built from the line type, the speaker and the mission tag
([bark banks](../file-formats/bark-banks.md#the-bank-number)). Flora is character 19 and `A1BSQA_SE01`
is mission 1, so her offer-A bank is 33 × 42900 + 19 × 825 + 1 × 5 = **1431380**. That bank holds one
line per conversation step **(seen in data)**:

| Tag | Step | Sound id → subtitle |
|---|---|---|
| `BECKN` | the player within 5 m | `4905642` "Over here." |
| `GREET` | within 2 m | `4905904` "It's good that you're here." |
| `DIALA` | first press of use | `4905684` "I just learned of a large weapons supply. A man I know, he told me he has been hiding them at his ranch house." |
| `DIALB` | second | `4905724` "The APR has seized his home, you see. He is afraid they will find his guns and then things will get very bad around here." |
| `DIALC` | third | `4905754` "The weapons are hidden in his house. I am looking for someone willing to go there and destroy the cache. - Maybe you?" |
| `DIALD` | accepting | `4905774` "Bueno. We already have enough weapons around here!" |
| `EXITA` | walking off mid-pitch | `4905804` "Wait, there's more!" |
| `EXITB` | walking off later | `4905844`, no subtitle |
| `EXITC` | after accepting | `4905874` "Let me know how it goes." |

The debrief bank, 1431385, has `BECKN` "Hello.", `GREET` "Hola.", `DIALA` "How did it go? Did you find
the ranch house okay?", `DIALB` "So. No more guns for APR.", `EXITA` "Any news?" and `EXITB` "Too bad."

**Every buddy uses the same tags.** The tag says which conversation it is, and the speaker picks the
bank. So Flora's offer-A lines are about `BSQ03` only because Flora's A job is `BSQ03`.

The steps are `Common_MissionBriefings.SCRIPTEDPAWN_*` boxes. They are built on the engine's
`PawnInteraction` (the "use" prompt), `PlayBark`, `CheckProximityRadius`, `LookatTarget`, `UseSTP` and
`PopUpConfirmationMessageBox`. The last one asks, at the accept step, whether to drop a side quest
already running **(seen in data)**.

### The run, step by step

1. **The bar opens for business.** Between faction jobs the manager sets `W0_MikesPlace_BSQbriefings`
   and asks each buddy at the bar to pick a stage. Flora has done neither job, so she takes stage 1.
2. **Load the voice.** `BSQ_MissionBriefing` loads mission tag `A1BSQA_SE01` for Flora with
   `SetMissionBarkBankState`, then waits for the bank.
3. **Talk.** `BECKN`, `GREET`, then `DIALA`, `DIALB`, `DIALC`, each waiting for the player to press use.
   `DIALD` is the accept, and the `Accepted` pin fires as it starts.
4. **Accept.** The selection box broadcasts `SideMission_Accepted` with `BSQ03`, so
   `AcceptedMissionID = "BSQ03"`, and fires `Start_mission_BSQA`. `BSQ_Activate_and_Cancel` calls
   `BSQ03_Mission.In`.
5. **Arm the world.** `BSQ03_Mission` enables layer `Missions/BuddySideQuests/BSQ03` (the ammo pile
   spawns), sets objective `BSQ03_01` (journal text and the ranch marker), forces the pile into state
   0, and listens to it with a `CompoundObject` box.
6. **Do the job.** The pile reports `OnStateChange`. At state 1, destroyed, the graph disables the
   layer and sets objective `BSQ03_02`, which moves the marker to Mike's. It prints the debug line
   "Congratulation you destroyed the Ammo pile…" and fires `Succeeded`.
7. **Point back.** The manager sets `BSQ_MainObjectiveComplete = true` and has the buddies pick again.
   Flora takes stage 2.
8. **Debrief.** Back at the bar, `BSQ_MissionDebriefing` plays the 1431385 lines. On `Succeeded` it
   sends achievement data, then sets `BSQA_MissionCompleted` and clears both `BSQ_MainObjectiveComplete`
   and `W0_MikesPlace_BSQbriefings`. Flora goes to stage 5 for now. Last it calls
   `MissionCompleteBroadcast.SideMission_Complete`. Because the id contains `BSQ`, that calls
   `SetSidequestMissionState.Succeeded` and resets `AcceptedMissionID`.
9. **The next job.** The debrief closed the bar's offering mode, so nothing new is offered at once.
   When the manager reopens it, Flora picks again. With A done, she takes stage 3 and offers `BSQ15`
   with tags `A1BSQB_SE01` and `A1BSQB_SE02`. What reopens it was not traced; the lore notes say new
   offers appear after the player has been away from the bar for ten seconds **(inferred)**. `BSQ15`
   is the same pattern: a convoy instead of a pile, with objectives `BSQ15_01` and `BSQ15_02`.
10. **Cancel.** Accepting any other job while `BSQ03` runs makes `BSQ_Activate_and_Cancel` call
    `Cancel`, which turns the layer off. `BSQA_MissionCompleted` stays false, so Flora offers `BSQ03`
    again later.

All read from the graphs **(seen in data)**.

## Recipe

Three routes, cheapest first.

### Route A: new content in an existing buddy's slot

Keep Flora and her conversation machinery, and change what `BSQ03` asks for. Everything that makes
it a quest (the offer, the return, the next job) stays as shipped.

| # | File | Change | Tool | Status |
|---|---|---|---|---|
| 1 | a sector file at the new spot | the job's entities under layer `missions\buddysidequests\bsq03`: a target to destroy, kill or fetch | JackAll Map tab; layer through `_layout.xml` | **(untested)** |
| 2 | `worldsector4349.data.fcb` | remove `AmmoPile_21` from the layer | `_layout.xml` delete | **(untested)** |
| 3 | `world1.mapsdata.fcb` | move `Objectives.MissonObjective_5` to the new spot | JackAll fragment | **(untested)** |
| 4 | `bsq03_hiddenarmory.bsq03_mission.lua` | the new target's id in `self.ammopile`; for a kill, swap the `CompoundObject` watch for `HealthEvents`, as `BSQ_Template_Kill` does | text in a layer, `jackall-cli mod lint` | the loader takes edited source **(RE-verified)** |
| 5 | `oasisstrings` | `Objectives/BSQ03_01`, `_01_NBM_01`, `_02`: the new journal and objective lines | `oasisstrings.fragment.xml` | string edits **(proven)** |
| 6 | Flora's banks 1431380 and 1431385 | optional: new audio for the same sound ids, and new `Subtitles` text for those ids | `jackall-cli spk` for the audio, the string fragment for the text | bank rebuilding **(proven** for weapon sounds**)**; bark banks **(untested)** |

Without step 6, Flora still pitches the ranch house while the journal describes the new job.

### Route B: your own giver, with text instead of voice

A new NPC who gives a chain of jobs, using only pieces that need no new bark bank:

- **The giver** is a placed pawn: any archetype, in a layer that is on when the quest is available.
  A buddy archetype or a civilian works **(inferred)**.
- **The conversation** is a `PawnInteraction` box on the giver, for the use prompt, then a
  `PopUpConfirmationMessageBox` with `TitleLocId` and `TextLocId` set to your own `oasisstrings` keys
  for the pitch, with Yes and No. The cell towers use the same box for `SQ_CANCEL` **(seen in data)**.
- **The job** is a copy of `BSQ_Template_Destroy`, `_Kill` or `_Fetch` with your layer and entity
  filled in, plus one `ObjectiveState` per stage.
- **The return** is a marker at the giver, in `main`, with a `StateId` of its own. The job's last step
  sets that state, and a second `PawnInteraction` on the giver closes the job.
- **The next job** is a flag in `Globals` (for example `Globals.MyMod_Stage = 2`) that the giver's
  graph reads before offering anything. `Globals` is saved with the game
  ([script state](../engine-internals/domino-scripts.md#script-state-in-the-savegame)) **(RE-verified)**.
- **Bookkeeping**: `MissionAcceptedBroadcast.SideMission_Accepted` and
  `MissionCompleteBroadcast.SideMission_Complete` with your own id, so the rest of the game knows a
  side job is running. An id containing `BSQ` is routed to `SetSidequestMissionState`, which expects a
  buddy; choose an id without it.
- **The host**: a placed `DominoEntity` with `fileBoxPath` naming your graph and `hidStartOnLoad` set,
  as described in [route C of the mission page](./authoring-a-mission.md#route-c-a-contract-outside-the-shipped-graph).
  Guard against the graph starting twice, since the entity respawns with its sector **(inferred)**.

Every step of route B is **(untested)**. Nothing in it depends on an undecoded format.

### Route C: your own giver, with voice

Route B, with the conversation replaced by the `SCRIPTEDPAWN_*` boxes and a mission bark bank. This is
**blocked**: see below.

## Blockers

| Blocker | What it stops | What would clear it | Estimate |
|---|---|---|---|
| **Fixed bark tables.** A bark bank is numbered from a speaker (a fixed character table) and a mission tag (the fixed 165-entry `GetMissionFromTag` table). No free number gives a new character, or a third job, its own lines. | route C; a third voiced job for a buddy | an FCSE hook on the tag and character lookups that maps new names to unused numbers, plus new `.bank`, `.pfe` and `.spk` files and `SPBarkData.banklist` entries | 2–4 days to a first spoken line |
| **The selection box knows two jobs.** Stages 1–4 are hard-wired to A and B. | a third job per buddy without rewriting the stage machine | extend `…Pawnbrief_Selection` with stages for C; needs the first blocker for its voice | a day of graph editing after the first blocker |
| **No graph editor.** | comfort on large graphs | hand-written Lua in BlackBox's shape, checked with `jackall-cli domino check` and `mod lint` | none for route A or B |

`EXITB` in bank 1431380 plays sound `4905844`, which has no subtitle entry. Whether a line with no
subtitle simply shows nothing is untested; it matters only for route A's step 6.

## Open questions

- What `W0_MikesPlace_BSQbriefings` waits for: the manager sets it between faction jobs, but its
  exact inputs were not traced.
- Whether a non-buddy pawn accepts the `SCRIPTEDPAWN_*` boxes, or they assume a buddy's brain.
- What `SetSidequestMissionState.Succeeded` changes in the engine beyond the buddy's record.
- Whether a quest's `Globals` flags and the selection box's `BSQA_MissionCompleted` survive a reload
  identically. Both are saved, the box's flag through its graph's `LuaState`.

## Cheapest next experiments

One launch each of the GOG install (`"C:\Games\Far Cry 2\bin\FCSE.exe"`), on a world-1 save with
Flora unlocked and no faction job open, with DevTools' **Developer console** option on. Graph
commands are probably spelled `domino_<name>`; if one is not found, check `console_dump_elements`.
Do not save afterwards.

1. **Walk every stage of `BSQ03` from the console.** No files. At Mike's:
   `domino_forcebsq BSQA_SE01`. Expect Flora to beckon and pitch the ranch house. Accept, then type
   `domino_BSQ03_base` to complete the job without travelling. Expect the journal line "That ammo pile
   cooked off very nicely…" and the marker on Mike's. Talk to Flora: expect the debrief. Leave the bar
   for ten seconds, come back, and expect the `BSQ15` pitch; if it doesn't come, that answers what
   reopens the bar, and `domino_forcebsq BSQB_SE01` forces it. This shows the whole chain in a few
   minutes, and checks the `forcebsq` parameter syntax.
2. **Route A, retargeted pile.** One layer: a new ammo pile elsewhere under the `BSQ03` layer, the old
   one deleted, the ranch marker moved, `self.ammopile` changed. Then experiment 1's commands. Expect
   the marker and the pile at the new spot, and the same debrief after destroying it.
3. **Route B's conversation alone.** One layer: a graph that puts `PawnInteraction` on any pawn near the
   player and opens a `PopUpConfirmationMessageBox` with two new strings, started from the console
   with a `ConsoleCommand` box. Expect the use prompt, then the box with your text. That proves the
   voiceless giver before the rest of route B is built.
