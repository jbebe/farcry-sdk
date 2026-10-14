---
sidebar_position: 2
---

# Act 1: Leboa-Sako

Every mission of the northern map, in the order the campaign runs them: the opening, the first buddy,
Mike's bar, the six faction jobs, Goka Falls, and the attack on Mike's or the church. How a faction
job runs in general (briefing, the buddy's plan, the buddy's fight) is on [How the campaign
runs](./overview.md). Citations follow [Sources and citations](../sources.md); script citations name
the file in `domino\user\` and the handler.

## The taxi ride

No mission record; graph `openingsequence.taxiride`.

**The game states**

- **The ride.** The player rides in a taxi from the airport to Pala. A movie sequence drives the car,
  and numbered designer events stage what he sees (`openingsequence.taxiride:f_280_Out` and the
  events before it):

  | Event | What the script stages | What the driver says |
  | --- | --- | --- |
  | 20 "The Last Flight" | a plane takes off | "That's probably the last one out of this country. Full of rich people. Only money and connections get you out now." (`5160475`) |
  | 40 "Civilians and Refugees" | people walking toward the airport | "Everyone thinks the big planes are coming back." (`5160236`) |
  | 70 "Brush Fire" | fires by the road | his brother lost his bungalow to a fire "And now he's left the country." (`5160156`) |
  | 80 "The Checkpoint" | four mercenaries at a roadblock | he bribes them with a promise of cold beer (`5160345`); "These militia; they come into our country, steal everything, and leave us nothing." (`5160055`) |
  | 100 "APR Convoy" | an APR convoy passes | "A lot of troops nowadays." (`5160485`) |
  | 110 "Burning Wreckage" | mercs by a burning truck | "Just boys letting off steam, right? You remember how it is." (`5160045`) |
  | 120 "Lots of Boats" | armed boats in the harbour | "Everyone's trying to find a way out of the country. Only fools stay now." / "Fools and cab drivers." (`5160098`, `5160282`) |
  | 150 "Final Destination" | the hotel | "Okay sir, we're here. We made it alive." (`5160186`) |

  The driver's lines are his by scene (`scene sm00_se01_taxidriver_event_*`).
- **The radio.** Liberation Radio plays on the way: "A plague has gripped our beautiful country and
  its name is A-P-R." (`5119680`).
- **The collapse.** The player's malaria flares twice. At the hotel he collapses, and the screen
  goes white.
- **The orders.** The journal holds the player's orders from the first frame: "Find the Jackal and
  kill him." (`Objectives/A1SM01_04_NBG_01`; `openingsequence.taxiride:f_10_Out`).

## `A1SM01` TownEscape: the hotel and the town fight

Story mission; 0 diamonds; graph `a1sm01_townescape.a1sm01_mission`.

**The game states**

- **The hotel room.** When the scene opens it is midnight. The player lies on the bed, and the Jackal
  is already in the room (`f_63_Out`, `f_120_Out`). He reads the player's orders aloud, says "NOBODY
  kills me", quotes his book, fires him, and says "So long." (`4577324` to `4973352`, Jackal; `scene
  sm01_se01_jackc_brief_all`).
- **The morning.** A white-out follows, and it is 11:00. An RPG hits the room. The player is given a
  Star .45 and a machete, and the Jackal is gone (`f_166_Out` to `f_220_Out_0`).
- **The street fight.** The town's cease-fire zone is switched off, and UFLL and APR reinforcements
  pour into Pala (`f_3_Out` to `f_33_Out`). The journal: "There's fighting all over the streets. And
  it's only gonna get uglier. I gotta get outta town" (`Objectives/A1SM01_02`).
- **How it ends.** The escape always ends in a collapse: a scripted death, a malaria blackout, or a
  forced blackout two minutes after the aiming tutorial. Every ending completes the mission
  (`f_111_Out` onward). No branch fails.
- **The quadrant decides the lieutenant.** Which quarter of town the player last ran through decides
  whose camp he wakes in. North-west (the default) is Kankaras, north-east Carbonell, south-west
  Quiepo, south-east Purefoy (`f_187_Output_0` to `_3`).

**Implied**

- The player lies in the hotel for some time before the Jackal's visit. The taxi graph's last designer
  label is "EVENT 160 10 days later", which no shipped text shows (`openingsequence.taxiride:f_280_Out`).
- A cut objective, "Play dead. Hope for the best.", belonged to an earlier, playable version of the
  hotel scene (see [Cut and unused content](./cut-content.md)).

## The first buddy: the tutorial

Graphs `a1bu00_tutorial.*` and the four `A1BU01`–`A1BU04` story wrappers.

**The game states**

1. **Waking.** The player wakes in a cage at his new lieutenant's camp: Carbonell's fishing camp,
   Kankaras's slaughterhouse, Purefoy's lumber yard or Quiepo's arena
   (`a1bu00_tutorial.a1bu00_storymission:Start_tutorial_with_A1BU01` to `_04`).
2. **The lieutenant.** He blames the player for men killed at the hotel and puts him to work: "You
   got some of my guys killed at the hotel" (`4547638`, Kankaras); "You got one of my boys killed, you
   know..." (`4547717`, Purefoy). The camps differ in what they stock:
   Carbonell and Purefoy an RPG-7, Kankaras and Quiepo an LPO-50 flamethrower.
3. **Five phone calls.** They send him through the tutorial: fix the car, clear an outpost, sleep at a
   safe house, scout the rival camp, free the foreign captive held there, come back.
4. **The rival camp is always across the line.** Carbonell sends him to Purefoy's lumber camp and
   Purefoy to Carbonell's fishing camp; Kankaras and Quiepo send him to each other's.
5. **Pay and a pointer.** Back at camp the lieutenant explains that "The only currency worth anything
   is diamonds" (`4921591`, Quiepo), lets him keep a briefcase of them, and sends him to Mike's bar for
   medicine.
6. **The captive** is the player's first buddy. He notices the malaria and promises medicine at Mike's
   (`4901047`, Flora Guillen; `a1bu01_prison.a1bu01_mission`).

**How it turns out.** One ending: the buddy is freed. No branch handles the captive dying. A malaria
blackout before the rescue sends the player back to his cage, and the lieutenant scolds him: "You're
sick, son! Did you forget?" (`4923652`, Purefoy).

## The optional rescues `A1BU01`–`A1BU04`, and `A1BU08`

Buddy missions; mission table: `A1BU01` "Prison" and `A1BU02` "Arena" (Red), `A1BU03` "DunkCage" and
`A1BU04` "Slaughterhouse" (Blue).

**The game states**

- **The two unused camps** of the tutorial pair hold captives too, from the end of the tutorial until
  Goka Falls (`master_world1.world1:f_217_Out`, `f_218_Out`).
- **The doormen pass on rumours.** The UFLL doorman tells of captives in the APR's lumber camp and
  arena; the APR doorman of captives in the UFLL's fishing camp and slaughterhouse. "The APR's got some
  foreign chap locked up at the Lumber Camp. The higher ups are looking for someone to go break him
  out." (`4911986`, guard; `master_world1.world1_hqdoorman`).
- **Accepting** counts as taking a job for that faction (`Objectives/A1BU01_01`: "I'll earn points with
  the UFLL if I can break this hostage out").
- **`A1BU08` "MountainCessna".** It still runs, although its record is commented out as a "teleported
  mission" (`master_world1.world1:f_240_Started`). A plane-crash survivor lies on a mountain north of
  Pala: "There was a crash. The plane just fell apart." "I've been up here three days." (`4902097`,
  `4902101`, Michele Dachss). Healing him with a syrette unlocks him; killing him first ends the mission with
  nothing (`a1bu08_mountaincessna.a1bu08_mission:f_21_Out`, `f_6_Killed`).

**How they turn out.** Freed, or lost: every Act 1 rescue still open when Goka Falls ends is stopped,
and its captive removed (`master_world1.world1_disablebu0x`).

## `A1GM00` GrinDelivery: Mike's, Reuben and Father Maliya

Story mission; 0 diamonds; graph `a1gm00_grindelivery.a1gm00_mission`.

**The game states**

1. **Mike's bar.** Two buddies wait there: the rescued captive and a second one. Talking to the second
   switches on the rescue system: from now on a buddy can come for the player when he goes down
   (`Tutorial/TU63_MESSAGE`; `a1gm00_grindelivery.a1gm00_mission`, `BuddyRescue.Activate`).
2. **Reuben.** He first takes the player for a soldier come for his tapes: "You people never stop.
   Here's the bloody tape." Then he sees the malaria and sends him to the church in Pala with a
   dictaphone for the priest (`4577380`, `4577402`, Reuben; `scene gm00_se01_greet_brief_a`). His
   longer talk, about the tapes the soldiers took and the Jackal's sixteen wars, is optional
   (`scene gm00_se01_brief_b` to `_d`).
3. **Father Maliya.** He trades the tape for his pills ("I think you should take one of those right
   away.", `4921347`) and asks for help with "folks trapped in Leboa-Sako" (`LoadScreens/
   MiSyn_A1GM00`). The script gives the player unlimited pills at this point (`SetMalaria.
   SetUnlimitedPills`).
4. **The end.** Leaving the church completes the mission. It also starts the Underground jobs and, if
   unlocked, the predecessor's tapes (`master_world1.world1:f_223_Out`).

## The six Act 1 faction jobs

All six are 15 diamonds, paid on acceptance. The APR's three are briefed by Kouassi at "the former
bordello" and the UFLL's three by Gakumba at "the abandoned bus station"
(`Objectives/A1LM00_02`, `A1LM00_03`). Each has a base job, a buddy's plan, and a buddy's fight at
the end of the plan. In every one, abandoning an accepted plan costs the buddy 2 history points.

### `A1LM01` CopKiller: the police chief (APR)

**The game states**

- **Briefing** (Kouassi, with Quiepo). The constable "says he is going to give his men to UFLL"; the
  APR wants him killed in public: "make it messy. We need you to send a message." Kouassi got the
  target from above: "Tambossa told us where you can find this guy." (`4557816`, `4557864`,
  `4557848`; `scene ml01_se01_boss_*`). Kouassi and Quiepo open the scene admiring a gun stamped
  "The Jackal", sold cheap (`4557681`, Kouassi; `4557685`, Quiepo).
- **Base job.** Ambush the chief's motorcade and kill him (`Objectives/A1LM01_01`).
- **Buddy's plan.** First take the chief's ledger of APR bribes from his brother at Shwasana. Then
  kill the chief at his police station, while the buddy hits the police armoury's guns on their way
  to the UFLL (`Objectives/A1LM01_03`, `A1LM01_04`).
  - The brother hands it over: "I'm sorry I ever agreed to keep it. Now leave my family in peace."
    (`4910169`, police chief's brother).
  - The chief, cornered: "You have already killed me. You might as well make it official."
    (`4910222`, police chief).
- **Buddy's fight.** The buddy ambushes the UFLL convoy carrying the police guns
  (`Objectives/A1LM01_05`).

**How it turns out.** The chief dies either way. On the plan, the buddy's fight follows
(`a1lm01_copkiller.a1lm01_mission:f_73_Out`). Hurting or killing the brother before he hands over the
ledger cancels the plan (`a1lm01_copkiller.a1lm01_intimidation:f_15_Out`).

### `A1LM02` ReapSew: the gas tankers (APR)

**The game states**

- **Briefing** (Kouassi, with Nick Greaves).
  - They open with television pictures of children dying in the camps. Kouassi: aid "has to come to US
    first. We know how to handle it." Greaves: "None of it will ever get to those poor sods."
    (`4559222`, `4559227`, Kouassi; `4559231`, Greaves).
  - The job: the UFLL holds natural gas the neighbouring country bought, to blackmail it for medicine.
    Blowing up the tank cars kills the deal: "Once trains are gone, hey! - No more medicine."
    (`4559291`, Greaves; `4559348`, Kouassi).
- **Base job.** Destroy the tanker at the railyard west of Pala.
- **Buddy's plan.** First kill a UFLL official at a cattle ranch who is holding back a troop train to
  rent the troops out. The troops then respond to the tanker attack, and the buddy derails their
  train and kills their ex-Gurkha commander (`Objectives/A1LM02_03`, `A1LM02_05`).
  - The official on the phone: "NO I don't know how to fire a gun, you stupid man!! That is not my
    job!!" (`4910253`, UFLL official).

**How it turns out.** The tanker is destroyed either way. On the plan, the derailed train stays where
it fell for the rest of the game; its layer is never switched off
(`a1lm02_reapsew.a1lm02_mission:f_34_Out`).

### `A1LM03` JunkyardDog: the motor pool (APR)

**The game states**

- **Briefing** (Kouassi, with Walton Purefoy). The scene opens on a rumour: Kouassi has heard the
  Jackal "got cancer, and only three months to live" and that "somebody was paid to come here and
  kill him", and Purefoy scoffs (`4557923`, `4557931`, Kouassi; `4557935`, Purefoy;
  `scene ml03_se01_*`). The job: wreck the air compressors of the UFLL motor pool at the landfill
  west of town.
- **Buddy's plan.** First steal the day's route map for the UFLL's disguised fuel trucks from the
  PetroSahel terminal. When the motor pool is hit, the buddy ambushes a tanker
  (`Objectives/A1LM03_03`, `A1LM03_05`).

**How it turns out.** The compressor is destroyed either way. On the plan, a fuel truck explodes near
the junkyard and the buddy fights its surviving driver and the UFLL troops who come
(`a1lm03_junkyarddog.a1lm03_explosion`).

### `A1LM04` DirectSpear: the foreign commandos (UFLL)

**The game states**

- **Briefing** (Gakumba, with Anto Kankaras).
  - A foreign Special Forces team, "paid to parachute in and grab someone", is camped in the
    south-eastern desert. Gakumba fears he will be blamed: "they will say: Gakumba is rich, taking
    their money and hiding it in secret bank accounts!" (`4558159`, Kankaras; `4558094`, Gakumba).
  - Kankaras: "They are here for a bigshot from the APR. But if they take our enemy, we got nobody to
    fight!" (`4558195`).
  - Gakumba: "This is OUR struggle. Africa is for Africans." (`4558255`).
- **Base job.** Destroy the commandos' equipment at the oasis.
- **Buddy's plan.** The buddy says the team is really hunting him, for stolen aid medicine. The
  player frightens their Belgian informant at a villa into radioing false coordinates, which sends
  the team into Mokuba shantytown, and destroys their gear there (`Objectives/A1LM04_03`,
  `A1LM04_04`).
  - The radio call is in the scene: "Bravo Papa Mike, this is Charlie 2-4. Do you read?" / "We copy.
    Go ahead." (`4921628`, the Belgian; `5143505`, radio voice).
  - The Belgian knows who the player is: "You're that guy, aren't you. The one that's looking for the
    Jackal." (`4910365`, the Belgian).

**How it turns out.** The team's gear is destroyed either way. On the plan, the buddy's medicine
burned with the commandos' truck. The APR blames him and pins him down at the crossroads
(`Objectives/A1LM04_05`). Hurting the informant before he makes the call cancels the plan
(`a1lm04_directspear.a1lm04_mission:f_15_Out`).

### `A1LM05` GrowOp: the OGC greenhouses (UFLL)

**The game states**

- **Briefing** (Gakumba, with Hector Voorhees). The scene opens on Bastion, the PMC the UFLL pays.
  Gakumba: "The money brought Bastion to Africa. No more money, no more PMC." Voorhees: "You paid us
  to do a job, eh? We're going to do it." (`4559398`, Gakumba; `4559394`, Voorhees;
  `scene ml05_base_*`). The job: the APR guards "some European agribusiness" in exchange for food;
  wreck its irrigation pumps (`4559434`, `4559455`, Voorhees).
- **Buddy's plan.** Take a canister of military defoliant from the chemical dump to the airstrip,
  where the buddy loads it into OGC's own crop-duster and sprays away the APR's cover
  (`Objectives/A1LM05_03`, `A1LM05_05`).

**How it turns out.** The pumps are wrecked either way. On the plan, the buddy is shot down and the
player must reach the crash site: "They shot my plane down! I'm surrounded!" (`4938827`, Warren
Clyde; `Objectives/A1LM05_06`). The wreck stays in the world. Killing the buddy at the airstrip cancels the
plan (`a1lm05_growop.a1lm05_mission:f_11_Out`).

### `A1LM06` OeduardRex: the King's gold (UFLL)

**The game states**

- **Briefing** (Gakumba, with Joaquin Carbonell).
  - The old King is back. Carbonell: "The hombre got a boatload of money from Europe, back in the day.
    And then he took off. But he's back and he brought his gold with him." (`4558425`).
  - Gakumba: "He has always played both ends against the middle. That is why he has the gold and we
    have this war." (`4558360`); "That gold comes from our mines. It belongs to us." (`4558452`).
  - The job is to kill the APR men guarding the gold at a north-eastern oasis and text its location
    (`4558534`, Carbonell).
- **Base job.** Find and tag the gold for the UFLL.
- **Buddy's plan.** Prince Oeduard wants his father dead and the signet ring that controls the
  family's accounts in Belgium. The player kills the King at the old fort, takes the ring, and hands
  it to the Prince by the lake (`Objectives/A1LM06_03`, `A1LM06_04`).
  - The King, in his last words: "If you kill me, he will avenge me. He loves me." (`4910407`, King).
  - The Prince, paying with an SUV: "Please, accept one of my vehicles as my gift." (`4910501`).
  - He takes his leave: "Do not come after me! I am much stronger than my father was!" (`4910530`,
    Prince Oeduard; his exit line when the player walks off).

**How it turns out.**
- **The base job.** The gold is tagged for the UFLL.
- **The plan.** Once the player is 150 m from the Prince, the Prince leaves the game, his order pulls
  the APR guards off the oasis, and the buddy goes ahead to the gold. Tagging it brings the APR back
  in three waves: "The APR figured out they've been screwed by the new King."
  (`a1lm06_oeduardrex.a1lm06_mission:f_80_Out`, `f_78_Out`; `Objectives/A1LM06_06`).
- **Killing the Prince** before that cancels the plan and brings the guards back (`f_55_Killed`).

**Implied**

- Nothing in the script betrays the player or the buddy. The one double-crossed is the APR: the
  Prince trades his father's gold to the UFLL for his inheritance.

## `A1SM02` FoolsErrand: Goka Falls

Story mission; 20 diamonds; achievement "killcaptain"; graphs `a1sm02_foolserrand.*`. Given by the Act
1 winner's boss.

**The game states**

- **The briefing.** The winning boss's greeting depends on whom the player last worked for. Kouassi
  greets a player who last worked for the UFLL with "Oh ho! Look who is here. The guy who works with
  everybody. - Well that's about to end." (`4558639`, Kouassi; `a1sm02_foolserrand.
  a1sm02_briefing_forblue:f_25_Out`).
  - Kouassi's pitch: "I want Gakumba dead. … He's at his cabin up on Thabamolaetsa." (`4972974`,
    `4558689`).
  - Gakumba's pitch: "The UFLL is almost unstoppable here - Even the party leader doesn't realize. So.
    I've decided to eliminate Kouassi" (`4558591`).
- **The kill.** The rival boss waits at the Goka Falls lodge, greets the player, and fights when
  provoked. Kouassi greets with "Little Big Man just got a little bigger." (`4559024`, Kouassi);
  Gakumba with "You. What are you doing here? Ah wait. Kouassi. Big man. Eh? BIG plans." (`4558946`,
  `4558951`, `4558955`, Gakumba).
- **The set-up.** After the kill, the employer's own men spring an ambush: "Now APR gunmen are coming
  out of the woodwork. I've been set up." (`Objectives/A1SM02_APR_02`; layer `A1SM02_BlueAmbush`).
  The designers' note reads "OMG!!1! A blue ambush!" (`a1sm02_foolserrand.a1sm02_mission_forblue`).
- **How it ends.** Escaping the area completes the mission. It also closes both HQs' doors for the rest
  of the act, stops every open rescue, ends the Underground jobs and removes Reuben from Mike's
  (`a1sm02_foolserrand.a1sm02_mission_forblue:f_30_Out`; `master_world1.world1:f_168_Out`).

## `A1SM03` DefenseRev: Mike's or the church

Story mission; 0 diamonds; graphs `a1sm03_defensereversal.*`.

**The game states**

1. **Reuben.** He calls from the deserted lumber camp: the winning faction has launched "a massive
   offensive in the northern district", foreigners are "declared spies", and soldiers are on their way
   to Mike's and to Father Maliya's church (`4911034`, `4911066`, `4911088`, Reuben).
2. **The choice.** The player commits to one by using the fridge at Mike's or the shrine at the church
   (`a1sm03_defensereversal.mikesbrief:f_48_Use`, `churchbrief:f_73_Use`). Whichever he picks first
   cancels the other.
3. **Mike's.** Every buddy he has is there, barricading the door with the fridge
   (`a1sm03_defensereversal.a1sm03_spawnbuddies`). "They hit us without warning… We barely got
   everyone inside." (`5160786`, Josip Idromeno).
4. **The church.** Father Maliya, an Underground operative and a woman slip out by the back. The
   priest asks the player to cover them: "Please, after we go, move the cabinet over the entrance. So
   they can't follow us." (`4909505`, Father Maliya).
5. **The assault.** Both assaults run three waves. Leaving the church in mid-fight is instant death
   ("You can't leave the church", `a1sm03_defensereversal.churchassault:f_21_Leave`).

**How it turns out.**
- **Either way the player loses.** After a minute or so scripted shots knock him out, and the mission
  completes (`mikesassault`, `churchassault`). At Mike's, if his last buddy goes down first, the
  script kills him at once ("Last Buddy down. You must die.", `mikesassault:f_12_LastBuddyInBuddyDown`).
- **The other place is not destroyed.** Nothing in the scripts destroys the place he did not defend
  or kills anyone there. The buddies at Mike's are removed when the mission ends, not killed.
- **Only defending Mike's calls the engine's "betrayed buddies" step**
  (`defencereversal.lua`, `SetDefenceRevesalBetrayedBuddies`).

**Implied**

- The engine's betrayed-buddies step after Mike's is what turns those buddies against the player in
  the finale. Nothing in the scripts says which buddies it marks.
