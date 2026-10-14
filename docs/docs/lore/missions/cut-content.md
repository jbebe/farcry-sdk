---
sidebar_position: 6
---

# Cut and unused content

What the mission scripts, the mission table and the string table still hold of missions, steps and
lines that the shipped game never reaches, and the slips that change what a player sees. "Never
plays" means no script, bark request or scene animation was found that uses it; the engine could in
principle still reach it on its own. Citations follow [Sources and citations](../sources.md).

## Missions cut or merged

| What | What is left | What became of it |
| --- | --- | --- |
| `A1SM04` | only a debug shortcut name, `StartA1SM04` (`master_world1.world1`) | *implied:* the old name of `A2SM04`, the mass-grave truck, which is still recorded as an Act 1 mission |
| `A2SM10` / `A3SM10`, "Escape Defection" | layers `A2SM10/A3SM10` and `A2SM10/Mission_Music`, which no script uses; `A2SM09` plays the scene `A2SM10.SE02_EscapeDefection_233` and loads the guard bank `A3SM10_SE03` (`a2sm09_prison.a2sm09_mission:f_13_Out` onward) | folded into `A2SM09`, the prison. *Implied:* it was numbered for Act 2, then Act 3, and `A3SM11` was once `A2SM11`: its triggers are still named `PTrigger_A2SM11_*` |
| `A3SM16` "NobleSacrifice" | the commented-out record "merged in A3SM15"; the packages `PKG_A3SM16_*`, the prefabs, the bank `A3SM16_SURV` and the chapter text `LoadScreens/MiSyn_A3SM16` | merged into `A3SM15`: the Jackal's hut, the choice and both endings |
| `A2BU05` "BlackMamba" | the record, commented out "discover to unlock"; three layers; the journal "ASSIGNED_A2BU05 is hurting bad from a snake bite… Can't even handle injecting the antidote." (`Objectives/A2BU05_01`); a voiced bank for all six southern buddies: "A snake bit me… Poisonous… Anti-venom…" (`4547845`, Andre Hyppolite) | cut. No script starts it. Its northern twin `A1BU08`, the plane-crash survivor, is also commented out ("teleported mission") but still runs (`master_world1.world1:f_240_Started`) |
| The "buddy" contract | the layers `Target_Buddy` and `Envelope_Buddy`; a reward setting `AssassinationRewardBuddy="20"` that nothing pays; the last southern contract's target is still named `Assassination_Target_Buddy` | turned into an ordinary cell-tower contract |
| The predecessor's invitation tape | the tape "The Predecessor": "Gives details on where to meet him and the Jackal." (`JackalTapes/C0V0M3_DESC`), never given | *implied:* replaced by the phone call `5123973` |
| The Ubidays demo | layers `Missions/UBIDAYS/*` in Bowa-Seko, eight of them on by default; graphs `ubidays.*` and a briefing bank `UDLM00_SE02` in which Frank Bilders fuses the radio and pipeline jobs: "That antenna's good for more than just music… Water company's hired local guns for security." (`5104920`, Frank Bilders) | a show build's mission, left in the data. The journal still has its text (`Objectives/UbidaysLongDemo_*`) |

## Steps cut from shipped missions

**The game states**

- **"Play dead."** The journal has an objective for the hotel room, "Play dead. Hope for the best." with
  "TWO uninvited guests" (`Objectives/A1SM01_01`, `A1SM01_01_NBG_01`). No script sets it.
- **"10 days later."** The taxi graph's last designer label is "EVENT 160 10 days later"
  (`openingsequence.taxiride:f_280_Out`). No shipped text shows a time skip.
- **The lieutenant's own rescue orders.** "Carbonell wants me to break out a prisoner being held captive
  by the APR at the lumber camp." (`Objectives/A1BU01_03`, and the same for the other three) is set by no
  script; the doormen offer the rescues instead.
- **The march through the storm.** Thirteen lines of a lieutenant driving his men through the
  sandstorm, used by no scene, bark or script: "Stay together! We don't want to lose anyone in this
  storm!" (`4558757`); "Where's Mutara? Is he still with us?" (`4558761`); "You stay with me, mate, if you
  want to get through this." (`4558831`); "Focus, you bloody animals! It's just a sandstorm." (`4558835`).
- **The airfield.** In `A2SM08` the best buddy's plan to take the diamonds to a plane plays, but its
  output is wired to nothing (`a2sm08_warlordassassination.a2sm08_mission_forblue`, box 12). The plane, the
  airfield and the capture exist only in dialogue.
- **The opening journal entries** of `A2SM08` and `A3SM12` ("…wants to see me at … headquarters in Port
  Selao", `Objectives/A2SM08_APR_01`; `A3SM12_APR_01`) are set by no script.
- **The dog tags.** Both `A2SM08` death scenes hold dog-tag triggers that no script reads
  (`PTrigger_DogTag`, `PTrigger_RedDog_Tag`).
- **A gun in the shelter.** `A2SM05` shows a Desert Eagle or an AK-47 by the canteen and then hides it;
  the player gets a machete (`a2sm05_reprisal1.a2sm05_briefing_forblue`).
- **Something to pick up in the briefing.** The briefing graphs pass a pick-up animation that the
  shared briefing box never reads: a whisky bottle (`A1LM01`, `A1LM02`), a cigar (`A2LM11`, `A2LM12`),
  a photo frame (`A1LM04`), and the placeholder "*** replace me ***" in seven others
  (`common_missionbriefings.basebrief_robust_disrupt`; `a2lm07_radioarmageddon.a2lm07_briefingbase`).
- **A glider to the kilns.** `A2LM10`, recorded as "FlyingJackal", keeps an unused variable
  `PlayerInGlider` (`a2lm10_flyingjackal.a2lm10_mission`). Nothing in the shipped job flies.
- **A plane over the airstrip.** `A1LM05` has a "first airplane flyby" trigger with no target and a
  delay wired to nothing (`a1lm05_growop.a1lm05_mission:f_73_Enter`).
- **The nag on the road.** Six "where are you?" calls for `A2LM09`'s truck drive, one per buddy, are
  wired to nothing: "Okay, I know I said to take it slow... It didn't mean short-bus slow." (`4934810`,
  Frank Bilders).
- **Briefcases for the Underground.** Eight `GrinMissions/Briefcase_*` layers, one holding a passport
  briefcase, are used by no script.
- **Envelopes for the contracts.** The contract script still works out an envelope layer per target
  and then discards it (`sidemissions.setassassinationvariables:f_3_Out`).
- **Doctor Obua and the tapes.** A layer `CarverTapeMissions/DoctorObua` exists and is used by nothing.

**Implied**

- The hotel scene was once playable. Contracts were once picked up as envelopes, and the Underground's
  jobs once involved finding briefcases. Obua may once have listened to the tapes in the south.

## Lines that never play

- **The taxi driver's second take.** About 26 lines (`5160070` to `5160521`) and one first-take line
  (`5160117`) belong to no scene.
- **The Jackal in the shelter.** "Doesn't matter who's in charge. It's always the same." (`4973114`) and
  two takes of his UFLL-variant opening (`4577638`, `4577643`).
- **The prison guards spotting the player.** "It's the prisoner!" (`4561503`), "Shit! Stop him!"
  (`4561508`). Only their small talk is requested.
- **The warlords' last shout in the finale.** "DROP him!" (`4909844`, Greaves), "You piece of SHIT--!"
  (`4910078`, Quiepo).
- **Frank's cancel call in `A2LM10`.** "You getting squeamish about my motives? Thanks for nothing,
  mate." (`4547929`, Frank Bilders). Its slot plays his away-timer line instead.
- **A courier's alarm in `A2LM10`.** "What's going on out there? … Where did I put it...?" (`5128024`).
- **Act 1 phone lines**, replaced by lines from other jobs. In `A1LM02`, for example, Josip Idromeno
  speaks his `A1LM03` lines about the ranch, the trucks and the tanker driver.
- **The second purge's guard.** "I think maybe you misunderstand." (`4561919`, guard). The bar's guard
  plays the first purge's bank instead.
- **A multiplayer leftover.** "Good! Now capture their canister!" (`5172698`) predates the diamond case.

## Slips that change what the player sees

**The game states**

- **Reuben's tape count is reset.** `A3SM14` sets the tape count to zero before Reuben reads it
  (`a3sm14_heartofdarkeness.a3sm14_mission:f_3_Out`); see [Act 3](./act-3.md).
- **Crates on the runway either way.** `A2LM12` leaves the air drop's crates on the airfield whether
  or not the drop happened (`PersistantAirportEntities`, `a2lm12_bunkerbuster.a2lm12_mission`, close-out).
- **A dead check at the Marina.** One of the three places that count as "back at base" during a buddy's
  fight is a rock, not a trigger, so it never fires (`StaticObject_10013`, `common_customboxes.post_libmission_w2`).
- **The wrong call.** In `A2LM07` the betrayed buddy's last call plays the "plan cancelled" line first
  (`4935535`, Andre Hyppolite) (`a2lm07_radioarmageddon.a2lm07_mission:f_30_Out`). In `A1LM01`, `A1LM02`,
  `A1LM04` and `A2LM11` the dead-line call is issued twice; whether both play is not known.
- **A fire that stays lit.** In the UFLL variant of `A2SM08` one burn emitter is switched on instead of
  off and keeps burning (`a2sm08_warlordassassination.a2sm08_mission_forred:f_45_Out`).
- **The Underground's last award.** The achievement for all Underground locations is granted when the
  player is too infamous, not when all are found (`undergroundlocation_all`,
  `sidemissions.grin_missions:f_225_Out`).
- **A side quest's layer stays on.** `BSQ08` never switches its layer off on success
  (`bsq08_theprototype.bsq08_mission`).
- **Music for the wrong world.** `A2SM09` sets world 1's music state in Bowa-Seko
  (`a2sm09_prison.a2sm09_mission:f_9_Out`).

## Designer notes left in

Debug text that the shipped scripts can still print, a selection:

- "OMG!!1! A blue ambush!" (`a1sm02_foolserrand.a1sm02_mission_forblue`)
- "You're... too infamous for your medicine, too infamous for your medicine." (`sidemissions.grin_missions`)
- "You killed the target. You are one cold-blooded mofo." (`sidemissions.assassination_missions`)
- "Ohhh yeahhh, it's ON, mofo!" (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_buddiesfight`)
- "Lieutenant at HQ variable not set. This will cause problems. Show this to James."
  (`a3sm12_housecleaning1.a3sm12_assassination_forblue:f_3_A_neq_B`)
- "You dont even have a buffer buddy ******WRONG******" (`a2sm09_prison.a2sm09_mission:f_1_Is_nil`)
- "Trop tard, le politician a décidé de se battre" ("Too late, the politician decided to fight"),
  in a job with no politician (`a2lm07_radioarmageddon.a2lm07_assassination`)
- "You found a tape! It says: "Blah blah the Jackal blah blah Moroccan blah"", over each of the
  predecessor's tapes (`partnerspecialmissions.mission_01_02:f_11_Out`)

Every mission also registers debug console commands that skip to its end, such as `A2SM04_meetcarver`,
which jumps straight to the Jackal scene, and "Carver" throughout is the Jackal's internal name.
