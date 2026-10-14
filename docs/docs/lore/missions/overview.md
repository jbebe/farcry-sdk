---
sidebar_position: 1
---

# How the campaign runs

Every mission in Far Cry 2 is a [Domino graph](../../engine-internals/domino-scripts.md), and one master
graph per world starts them: `master_world1.world1` for Leboa-Sako, `master_world2.world2` for
Bowa-Seko. This page covers the shape of the whole campaign and the machinery every faction job
shares. The missions themselves are on the act pages, and the engine's rules for offering missions,
reputation and rewards are on [The Campaign](../../engine-internals/campaign.md). Citations follow
[Sources and citations](../sources.md).

## The kinds of mission

The mission table in `gamemodesconfig.xml` lists them with internal names, factions (Blue is the APR,
Red the UFLL) and diamond rewards.

| Kind | Ids | Count | Given by |
| --- | --- | --- | --- |
| Story | `A1SM01`, `A1GM00`, `A1SM02`, `A1SM03`, `A2SM04`–`A2SM09`, `A3SM11`–`A3SM15` | 15 | the master graph, the moment the engine selects one; most are briefed inside their own graph |
| Faction jobs ("library missions") | `A1LM01`–`A1LM06`, `A2LM07`–`A2LM12` | 12 | the boss in a faction HQ, through its doorman |
| Buddy rescues | `A1BU01`–`A1BU04`, `A1BU08`, `A2BU06`, `A2BU07` | 7 | the opening, the HQ doormen, or found in the field |
| Buddy side quests | `BSQ01`–`BSQ26` | 26 | a buddy at Mike's bar (north) or the Marina (south) |
| Underground jobs | four cells per world | 8 | Father Maliya or Doctor Obua and the Underground operatives |
| Arms-vendor convoys | `A1CV01`–`A1CV04`, `A2CV05`–`A2CV08` | 8 | the weapon-shop vendor |
| Assassinations | six per world, drawn at random | 12 | an anonymous voice on the phone, through a cell tower |
| Jackal tapes and predecessor tapes | 17 and 8 tapes | — | found in the world |

See [Act 1](./act-1.md), [Act 2](./act-2.md), [Act 3](./act-3.md), [Side missions](./side-missions.md)
and [Cut and unused content](./cut-content.md).

## The order

**The game states**

The master graph re-runs the engine's mission selection after every story mission and after every
faction job, then starts or offers what comes back (`master_world1.world1:f_205_Out`, `f_203_Out`).
Story missions start at once; faction jobs are armed at their HQ; buddy rescues go to the doormen
(`master_world1.world1:f_203_Out`, `master_world1.world1_hqdoorman:NewMissionSelected`). In order:

1. **The taxi ride** to Pala, then **`A1SM01`**, the hotel and the escape from town.
2. **The first buddy rescue**, which is also the tutorial, in the camp of the lieutenant who takes the
   player in.
3. **`A1GM00`**: Mike's bar, Reuben, Father Maliya and the first malaria pills.
4. **The six Act 1 faction jobs**, three for each side, with the optional buddy rescues in between.
5. **`A1SM02`** at Goka Falls, **`A1SM03`** at Mike's or the church.
6. **`A2SM04`**, the mass-grave truck and the sandstorm; **`A2SM05`**, the revenge killing at the
   victory rally; then the road south into world 2 (`master_world1.world1:f_133_Enter`).
7. **`A2SM06`** and **`A2SM07`**, the arms barge.
8. **The six Act 2 faction jobs**, with the two Act 2 buddy rescues.
9. **`A2SM08`** (the peace letter), **`A2SM09`** (the prison), **`A3SM11`** (the second warlord).
10. **`A3SM12`** and **`A3SM13`** (the purges), **`A3SM14`** (the airport), **`A3SM15`** (the end).

Underground jobs, convoys, assassinations, side quests and tapes run alongside, each started and
stopped by the master graph (see [Side missions](./side-missions.md)).

## Which variant you get

**The game states**

- **Your first lieutenant** is whoever's men find you where you collapse at the end of `A1SM01`. The
  last town quadrant you entered decides it: north-west Kankaras (also the default), north-east
  Carbonell, south-west Quiepo, south-east Purefoy (`a1sm01_townescape.a1sm01_mission:f_187_Output_0`
  to `_3`). He stays "your" lieutenant for the whole game (`common_customboxes.set_helperfromstory`).
- **The faction that wins Act 1 or Act 2** is read from the engine
  (`GetWinningFactionPerAct`). No mission script sets it; only a debug shortcut writes it
  (`domino\system\bypass_setwinningfaction.lua`). A missing value counts as a UFLL win
  (`master_world1.world1:f_197_Out`).
- **Who briefs which story mission** follows from those two:

  | Mission | Given by |
  | --- | --- |
  | `A1SM02` Goka Falls | the Act 1 winner's boss |
  | `A2SM05` revenge, `A2SM06` arms barge | the Act 1 loser's lieutenant |
  | `A2SM08` peace letter | the Act 2 winner's warlord |
  | `A3SM11` second warlord | the Act 2 loser's lieutenant |
  | `A3SM12` first purge | your first lieutenant, in his own faction |
  | `A3SM13` second purge | his rival, in the other faction |

  (`master_world1.world1:f_195_*`, `f_199_*`; `master_world2.world2:f_206_*`, `f_199_*`, `f_203_*`,
  `f_224_*`, `f_223_*`.)
- **The story's name templates** are filled in when `A1SM02` and `A2SM08` start
  (`master_world1.world1:f_176_Started`, `master_world2.world2:f_160_Started`):

  | Winner | `WINNING_FACTION_BOSS` | `_HELPER` | `LOSING_FACTION_BOSS` | `_HELPER` |
  | --- | --- | --- | --- | --- |
  | APR, Act 1 | Prosper Kouassi | Nick Greaves | Leon Gakumba | Hector Voorhees |
  | UFLL, Act 1 | Leon Gakumba | Hector Voorhees | Prosper Kouassi | Nick Greaves |
  | APR, Act 2 | Oliver Tambossa | Nick Greaves | Addi Mbantuwe | Hector Voorhees |
  | UFLL, Act 2 | Addi Mbantuwe | Hector Voorhees | Oliver Tambossa | Nick Greaves |

**Implied**

- The reprisals and the barge job come from the side that just lost: its lieutenant hires the player
  to hurt the winner.
- What decides the per-act winner is engine code nobody has traced. It cannot be "who you worked for
  most", since every faction job of the act is done before `A1SM02` asks.

## How a faction job runs

**The game states**

1. **The HQ.** The doorman frisks the player ("You know the drill. No weapons.", `4912500`, guard)
   and lets him in (`common_hq_doorman.hqdoorman_basebriefing`). Inside, the boss and his lieutenant
   are talking; walking in interrupts them (`common_missionbriefings.basebrief_convo`).
2. **The greeting.** The boss greets the player with one of six lines chosen by the engine from the
   player's history with him, including one for a player who worked for the enemy: "Hey, greedy!
   What's the big idea, running action for the UFLL?" (`4888895`, Kouassi;
   `common_missionbriefings.basebrief_greet:f_4_Betrayed`).
3. **The pitch.** The boss and lieutenant explain the job. Grabbing the folder or walking out cuts
   them short, with an annoyed line from the boss ("You're hurting Kouassi's feelings.", `4557907`,
   Kouassi; `scene ml01_se01_boss_abort_ab`) (`common_missionbriefings.basebrief_robust_offer`).
4. **Accepting** is interacting with the lieutenant, who hands over the file. The job's diamonds are
   paid at that moment, not on completion (`common_missionbriefings.basebrief_pickup_recap_accept`;
   `common_customboxes.missionacceptedbroadcast:f_20_Out`).
5. **The warning.** Every lieutenant's parting line says the job is off the books: "This is a
   classified mission. That means everybody's an enemy. Even APR." (`4973254`, Greaves;
   `scene ml02_se01_helper_exit`); "This is secret mission! So UFLL guys don't know you." (`4973010`,
   Kankaras; `scene ml04_se01_helper_exit`).
6. **There is no refusal.** Walking out simply ends the scene. On the next visit the boss and guard
   are gone and only the lieutenant waits with the folder
   (`common_missionbriefings.basebrief_init:f_24_True`, `basebrief_exit_refuse`).

**The buddy's plan.** If the player has a best buddy, he waits at a nearby safe house and phones 30
seconds later: he "claims to have more information" (`Objectives/A1LM01_02`). Hearing his second line
of dialogue accepts his plan; there is no yes-or-no (`common_missionbriefings.briefing_subvert:
f_29_Dialog_started`). The plan adds steps that do more damage to the job's target and ends with the
buddy in a fight of his own. Abandoning the plan after accepting it brings an angry call — "What
happened? Cold feet?" (`4938897`, Warren Clyde) — and costs the buddy 2 history points
(`a1lm01_copkiller.a1lm01_mission:f_19_Out`).

**The end of a job.** On the buddy's path every faction job does the same, in this order: mark it
subverted, mark it completed, give the next safe-house upgrade, then start the buddy's fight
(`a1lm01_copkiller.a1lm01_mission:f_71_Out` to `f_67_Out`, and the same in all twelve). The reward
and the credit are never at risk in the fight that follows.

## How a buddy fares in his fight

**The game states**

The buddy's fight (`common_customboxes.wager_v3`) runs on a 15-minute timer, five minutes in
`A2LM09`. However it ends, the next buddy the player meets at a safe house comments on it
(`savepoints.savehouse_bark_selectificationalizer`):

| What happens | Result | What the next buddy says |
| --- | --- | --- |
| The player helps him win, or heals him when he is down | he lives | thanks or praise: "He is alive today because of you." (`5109828`, Hakim Echebbi) |
| He goes down and the player puts him out of his misery | he dies | condolence: "I heard you did what you could for him." (`5009634`, Marty Alencar) |
| The timer runs out first, the player leaves him down more than 256 m away, or goes back to Mike's, the town or the next world before the fight ends | he dies, betrayed | blame or gossip: "Your pal Andre got a raw deal, didn't he?" (`5107293`, Frank Bilders) |

A betrayed buddy's last act is a phone call that cuts off: "You left me here to die. You are the
lowest form of scum -" (`5109611`, Quarbani Singh); "Why didn't you come? Why did you leave me here
to die like this? I thought we were friends -" (`5009883`, Xianyong Bai)
(`common_customboxes.wager_v3:f_62_Out_1`). A buddy who comes through says one of his own lines when
the player walks up, for example "Thanks man. We're good… See you at Mike's." (`4937585`, Warren
Clyde).

**Implied**

- These three results are the `SRV`, `LOS` and `ABN` comment banks the [characters
  page](../characters.md#the-buddies) reads from the dialogue alone. The scripts confirm that reading.

## Safe-house upgrades

**The game states**

Every completed buddy plan upgrades every safe house of that world. The order depends on how many
plans are done, not which (`common_customboxes.subvertrewardw1`, `subvertrewardw2`):

| Plan | Leboa-Sako | Bowa-Seko |
| --- | --- | --- |
| 1st | a vehicle | a full health station |
| 2nd | a syrette pickup | an ammo pile |
| 3rd | a full health station | the vehicle's mounted weapon upgraded |
| 4th | an ammo pile | a fuel pile |
| 5th | a fuel pile | an explosives pile |
| 6th | the vehicle swapped for an armed one | the mounted weapon upgraded again |

Meeting the second buddy at a safe house is what lets him come and rescue the player when he goes down
(`savepoints.savepoints:f_108_Greet_finished`).

## Travel

**The game states**

Each world has five bus stations: the town and four outlying stops, in map squares B2, B4, D2 and D4.
A ride asks for confirmation, takes four hours of game time and ends in an autosave
(`fasttravel.fasttravel:f_96_Use` to `f_248_Out`).
