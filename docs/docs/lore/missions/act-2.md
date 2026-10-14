---
sidebar_position: 3
---

# Act 2: the road south and Bowa-Seko

Every story mission and faction job from the mass-grave truck to the prison escape. The first two
still run on the northern map; the rest run in Bowa-Seko. How a faction job runs in general is on
[How the campaign runs](./overview.md). Citations follow [Sources and citations](../sources.md);
script citations name the file in `domino\user\` and the handler.

## `A2SM04` ExileWorld2: the mass-grave truck and the sandstorm

Story mission, recorded as Act 1; 0 diamonds; graph `a2sm04_exileworld2.a2sm04_mission`. Runs in
Leboa-Sako's northern desert (`w1_e_2`). No one gives it: the master graph starts it as soon as
`A1SM03` ends.

**The game states**

1. **The truck.** The player wakes among corpses in a truck. The journal: "Leboa-Sako fell to the
   APR, and me with it. They're hauling the dead out to some mass grave in the desert… they must
   have thought I was dead" (`Objectives/A2SM04_APR_01`; the UFLL text if the UFLL won). All his
   weapons are taken (`ManageInventory.RemoveAllWeapon`, `f_0_Out_1`).
2. **The storm.** He falls out of the truck into a sandstorm with one order: "Get to shelter."
   Reaching the shack, wandering out of the area or 90 seconds completes the mission, and he collapses
   (`f_132_Out` to `f_149_TimeElapsed`).
3. **The Jackal.** He wakes on a bed in the shelter with the Jackal beside him: "Stop squirming."
   (`4577554`, Jackal; `scene sm04_se03_jackc_greet_aa`). The Jackal says the player is "stronger than
   I thought… But not strong enough to stop the APR" (or the UFLL) (`4577582`, `4577591`, `4577647`,
   Jackal), then gives the disease speech: "It's a goddamn disease is what it is. It's a cancer… Every
   cell is infected." (`4577606`, Jackal); "And the longer you stay, the deeper the virus goes… Quarantine
   the patients." (`4577598`, Jackal); "You stay here." (`4577655`, Jackal).
4. **He leaves.** The Jackal's layer is switched off when his exit animation ends
   (`f_89_Finished` onward). He does not harm the player.

**How it turns out.** One ending. The next chapter's synopsis: "After the Jackal dragged me out of
that sandstorm and took off…" (`LoadScreens/MiSyn_A2SM05`).

## `A2SM05` Reprisal1: the victory rally

Story mission; 0 diamonds; graphs `a2sm05_reprisal1.*`. Still in Leboa-Sako. Given by the Act 1
loser's lieutenant in the same shelter: Greaves if the UFLL won, Voorhees if the APR won
(`master_world1.world1:f_201_A_eq_B`).

**The game states**

- **The map changes hands.** The mission's first act turns the whole northern map to the winner's
  colour (`ChangeMapArmy "allmap"`, `a2sm05_reprisal1.a2sm05_mission_forblue:In`).
- **The lieutenant's offer.** He has stumbled on the player mid-retreat. Greaves: "?...How did YOU get
  here?" (`4558769`); "Things have taken a turn for the worse… And my men are no use in their
  condition." (`4558791`); "Go back north and kill Gakumba. - It'll be easy. He's holding a troop
  rally. Standing on a truck. Wide open." (`4973102`) (Greaves; `scene sm05_se01b_greaves_*`).
  Voorhees: "Go back to Leboa and kill Kouassi… Standing on a truck. Fully exposed." (`4973280`,
  Voorhees); "We'll be at the Crossroads..." (`4558910`); "Piet, you're riding with me..." (`4558915`).
  The price is a truck: "We'll bring a truck around, leave it for you here at the shelter. But I
  require something." (`4558796`, Greaves).
- **A machete.** The player drinks from a canteen and is given a machete; nothing else
  (`ManageInventory.AddWeapon`, `a2sm05_reprisal1.a2sm05_mission_forblue`).
- **The rally.** The winning boss speaks from a truck through a megaphone. Kouassi: "This is OUR
  country now! APR! We make the rules!" (`4889055`); "…It's GOOD TO BE KING!!!" (`4972769`). Gakumba:
  "WE - have brought peace to this country! … Thanks to Gakumba - and Mbantuwe!" (`4559066`). He fights
  when attacked or when his men nearby are in combat (`a2sm05_reprisal1.a2sm05_warlordassassination`).

**How it turns out.** One ending: the boss dies. The journal sends the player to the lieutenant's
command post in Sefapane (`Objectives/A2SM06_APR_01`, `A2SM06_UFLL_01`). A trigger at the desert's
southern edge then moves him to Bowa-Seko (`master_world1.world1:f_133_Enter`, `f_131_Out`), which also
switches on the Underground's world-2 mode.

**Implied**

- The shelter holds the lieutenant's own gun, a Desert Eagle for Greaves or an AK-47 for Voorhees,
  which the script hides rather than gives. The player was probably once meant to take it.

## `A2SM06` HornetsNest: the arms barge

Story mission; 20 diamonds; unlocks a buddy; achievement "breakceasefire"; graphs
`a2sm06_hornetnest.*`. Given by the same lieutenant at his faction's command post in Sefapane, which
the designers call "Crossroads" (`master_world2.world2:f_208_A_eq_B`).

**The game states**

- **The briefing.** Greaves: "Welcome to Bowa-Seko. - Nice work, with Gakumba. The man deserved it."
  (`4559146`). His plan is his own: "The big boss, Major Tambossa, doesn't know anything about it"
  (`4559163`). If the southern warlords join forces, "blokes like you and I are out of a job"
  (`4559170`). So: "your friend the Jackal's been hiding a massive weapons cache on his island. Lake
  Segolo. I've worked out a way to sail those weapons straight into the city - under a UFLL flag."
  (`4559182`) (Greaves; `scene sm06_se01apr_boss_*`). Voorhees makes the same pitch with the flags
  swapped, and adds that "Bastion UK packed up and left the country" (`4560354`) and that "It's the
  foot-soldiers that get lined up against the wall and shot. Every time." (`4560374`) (Voorhees).
- **The skipper.** At the island a stranger waits at the wheel: "I'm Andre Hyppolite. I'll be your
  captain tonight." (`4897158`, Andre Hyppolite); "Name's Frank Bilders. I'll be yer skipper today."
  (`4897305`, Frank Bilders). The engine picks which of the six southern buddies it is
  (`AssignBuddy`, `a2sm06_hornetnest.a2sm06_mission:f_0_Out`). He knows what the cargo will do: "what we
  WILL serve is a huge steaming pile of semi-automatic weapons to the poor souls living in Port Selao.
  And that will be the death of that place." (`4897256`, `4897261`, Frank Bilders).
- **The run.** Gunmen and boats attack the barge on the way to Port Selao. When it is hit, the engine
  stalls and the player repairs it. The barge cannot be lost (`a2sm06_hornetnest.a2sm06_bargesequence`).
- **The doctor.** At the dock the mission is already complete
  (`a2sm06_hornetnest.a2sm06_mission:f_14_Out`). The skipper sends the player to warn Doctor Obua at the
  clinic. Obua: "I'm Obua. I'm the only doctor here." (`4939212`); "Even this cease-fire was too much to
  ask for. At least *you* bothered to warn us." (`4939226`); "If you return here - when it is calm - I
  would be happy to introduce you." (`4939244`) (Doctor Obua).
- **The ceasefire breaks.** The street fighting switches on as Obua finishes greeting the player, and
  the capital's ceasefire zone switches off after his talk
  (`a2sm06_hornetnest.a2sm06_doctorbriefing:f_2_Greet_finished`; `a2sm06_mission:f_38_Completed`). A
  second stranger at the clinic shows the player the back door (`Objectives/A2SM06_APR_05`). The
  player then fights his way out of town.

**How it turns out.** One ending. The journal: "fighting between the militia broke out over the big
boat load of guns that mysteriously appeared down in the docks" (`Objectives/A2SM06_APR_05`). The
shop's world-1 guns unlock when it closes (`master_world2.world2:f_138_Closed`).

**Implied**

- The skipper is the Act 2 buddy this mission unlocks, and `A2SM07` "JacksBuddy" is named after him,
  the man who carried the Jackal's guns. The designers' notes call him Marty Alencar whoever he is.

## `A2SM07` JacksBuddy: the barge chase

Story mission; 0 diamonds; achievement "buddyunlocked"; graphs `a2sm07_jacksbuddy.*`. Starts at once
after `A2SM06`.

**The game states**

- **The call.** The skipper phones: "The Lieutenant's plan worked. The guns are in Port Selao. But now
  I've got gunboats on my ass." (`4935505`, Andre Hyppolite). A minute later: "now I'm taking fire from
  two directions! Both factions at once!" (`4935625`, Frank Bilders).
- **The fight.** The player kills the attackers at the old marina in the inlet. The journal: "If either
  side figures out what we pulled, things could get bad. I better intercept them and make sure no one
  talks." (`Objectives/A2SM07_01`).
- **The marina.** The skipper decides to stay: "This barge is useless now. I might as well sink it and
  take a look around… fate must have brought me here for a reason, right?" (`4897680`, Andre
  Hyppolite); "Reckon I'll be feathering a new nest round here, like." (`4897834`, Frank Bilders);
  "Next time you come here, it's going to look totally different!" (`4897935`, Xianyong Bai).

**How it turns out.** One wired ending; the script handles no failure
(`a2sm07_jacksbuddy.a2sm07_mission`). The barge stays sunk in the inlet, the capital returns to its
ceasefire state, the faction HQs open and the marina becomes the south's Mike's
(`a2sm07_jacksbuddy.a2sm07_mission`, close-out; `master_world2.world2:f_139_Closed`).

## The Act 2 buddy rescues `A2BU06` and `A2BU07`

Buddy missions; graphs `a2bu06_oldbrewery.a2bu06_mission`, `a2bu07_dogonring.a2bu07_mission`.

**The game states**

- **The tips.** The APR doorman: "The UFLL is holding someone at the Brewery. Not sure who he is, but
  he's not local." (`4912289`, guard). The UFLL doorman: "The APR's got some foreigner locked up at
  the Dogon Sediko" (`4912280`, guard).
- **The cells** go live the first time the HQs open or a faction job is offered
  (`master_world2.world2:f_122_Out`, `f_214_Out`).
- **The captive** is a southern buddy, whom the engine picks. Opening his door unlocks him: "It's a
  fucking prison break." (`4902307`, Frank Bilders). He suggests splitting up and meeting at the bar.

**How they turn out.** One ending each, no failure path. The `A2SM08` briefing removes both captives
if they are still there (`ShutDownBuddies`, `master_world2.world2:f_61_Out`).

## The six Act 2 faction jobs

All six are 30 diamonds, paid on acceptance. The APR's three are briefed by Oliver Tambossa with Nick
Greaves, and the UFLL's three by Addi Mbantuwe with Hector Voorhees, in their Port Selao offices
(`<job>.briefingbase:In`). The buddy's plan and fight run as in Act 1 (see
[How the campaign runs](./overview.md)).

### `A2LM07` RadioArmageddon: Liberation Radio (APR)

**The game states**

- **Briefing.** The radio is playing when the player walks in. Tambossa: "You've heard the radio. It's
  outrageous, the lies." (`4559684`); "I have a plan to shut down Liberation Radio. Once and for all."
  (`4559689`). Greaves greets the player as "The Butcher himself" (`4559644`) and places the rig: "at
  nine a.m. this morning, they parked their station over Weelegol village." (`4559701`)
  (`scene ml07_se01_*`).
- **Base job.** Destroy the mobile transmitter's generator near Weelegol (`Objectives/A2LM07_01`).
- **Buddy's plan.** First kill the UFLL propaganda minister at the tourist resort, who "monitors the
  radio broadcasts with one hand on the kill-switch". Then threaten the DJ, Lord Haw-Haw, into reading
  a prepared message, and only then destroy the rig (`Objectives/A2LM07_03`, `A2LM07_04`,
  `A2LM07_06`).
  - The minister is watching cricket: "Uniyal... What are you thinking?!" (`4910541`, minister).
  - The DJ: "There is a broadcast delay! The censor will catch it." (`4910656`, DJ).
  - The broadcast: "There is an invader in our midst! … This person is disguised as a health official.
    They are here to poison our wells! … His SUV is on the highway south of Weelegol!" (`4910675`, DJ).
  - The "health official" is a foreign guest of the UFLL, and the buddy is his driver: "It will look
    bad for Mbantuwe when people hear my message and then rip his guest limb from limb" (`4933838`,
    Nasreen Davar).

**How it turns out.** The rig is destroyed either way, and the script switches the radio broadcast off
on both endings (`Radio.EnableRadioBroadcast`, `a2lm07_radioarmageddon.a2lm07_mission:f_62_Out`). On the
plan, the mob kills the official and pins the buddy down: "He's dead! - But I'm pinned down!"
(`4934602`, Andre Hyppolite). Killing or hurting the DJ before he reads cancels the plan
(`a2lm07_radioarmageddon.a2lm07_intimidation`).

### `A2LM08` PipeDreams: the water pipeline (APR)

**The game states**

- **Briefing.** The player walks in on an argument. Greaves: "You're in no shape to start a war with
  another country." (`4559807`). Tambossa: "WE will not be responsible for this horrible travesty."
  (`4559811`); "This will UNITE us. One Africa." (`4559819`). The job: "Addi Mbantuwe's people are
  pumping water over the border, in exchange for large cash payments." (`4559872`, Greaves). Tambossa
  wants it to look like "the work of a free agent" (`4559912`) (`scene ml08_se01_*`).
- **Base job.** Take dynamite from the TaeMoCo diamond mine and blow the pipeline at its construction
  site (`Objectives/A2LM08_01`).
- **Buddy's plan.** Every buddy worked security at the mine and will be blamed for the missing TNT:
  "since all that TNT was stolen on my watch" (`4932522`, Marty Alencar). The mine is a sham with no
  diamonds, and flooding it hides that: "Once TaeMo's flooded, it won't matter if people think the
  mine's pinched out or not. It'll be gone." (`4932434`, Marty Alencar). The player first wrecks the
  pump house's safety cutoff (`Objectives/A2LM08_03`).

**How it turns out.**
- **The base job.** The pipeline is breached. No flood.
- **The plan.** The breach floods the dry riverbed and the mine. The script raises the water level in
  more than twenty sectors and swaps the mine's layers for flooded ones, which are never switched back
  (`a2lm08_pipedreams.a2lm08_mission:f_17_A_is_True` onward). The buddy is caught at the mine: "The mine
  is flooding! It's beautiful!" (`4937896`, Nasreen Davar).

### `A2LM09` DentalPlan: the nitrous oxide (APR)

**The game states**

- **Briefing.** Greaves warns about the Jackal: "He didn't SELL the weapons this time. He GAVE them
  away." (`4560002`). Tambossa: "the people! They are no longer listening to us… THEY are the threat,
  Greaves. Not this Jackal person." (`4560022`). The job: the UFLL keeps nitrous oxide on a truck at an
  old dental clinic, and "This shortage helps us, frankly. Less surgeries, less soldiers surviving."
  (`4560086`, Greaves). Tambossa: "We will see how smoothly their surgeries go when the patients are
  still awake. Eh?" (`4560127`) (`scene ml09_se01_*`).
- **Base job.** Blow up the tanks on the truck.
- **Buddy's plan.** Fetch a part from the post office, repair the truck and drive it into the UFLL's
  garage in Sefapane, where the buddy works. He shorts the fuse box and the garage goes up
  (`Objectives/A2LM09_03` to `A2LM09_07`). Andre has a reason of his own: "I'm leaving my ID papers in
  there, where they WON'T burn. So when my… old employers find em, they'll assume I died in the fire"
  (`4887886`, Andre Hyppolite).

**How it turns out.** The tanks are destroyed either way. On the plan, the garage is destroyed for
good, and UFLL troops come "out for blood" with only five minutes on the buddy's clock
(`Objectives/A2LM09_08`; `a2lm09_dentalplan.a2lm09_mission:f_58_Out`).

### `A2LM10` FlyingJackal: the artemisia kilns (UFLL)

**The game states**

- **Briefing.** Voorhees and Mbantuwe argue about Bastion's departure: "Could be the best thing,
  really. I mean the hell with Bastion." (`4560575`, Voorhees); "They'll work for ME, you mean."
  (`4560588`, Mbantuwe). The job: "Turns out that the APR's got a lock on some sort of remedy."
  (`4560636`, Voorhees); "Artemisia. … Our medicine men know exactly how to use it." (`4560641`,
  Mbantuwe). The kilns are in the Dogon cliff village: "Burn the rest." (`4560701`, Voorhees); "Then OK,
  nobody gets medicine." (`4560689`, Mbantuwe) (`scene ml10_se01_*`).
- **Base job.** Destroy the kilns at the Dogon village.
- **Buddy's plan.** Kill an APR courier at an outpost and read his orders, which lead to the hidden
  stockpile. The buddy then raids it while the kilns burn: "plant a bomb in the artemisia stores. The
  APR's not only going to lose their ability to process this medicine. But their entire reserve as
  well." (`4933862`, Nasreen Davar).

**How it turns out.** The kilns are destroyed either way. On the plan, the APR's artemisia store, a
buried granary in Dogon ruins, blows up and burns, and its destroyed layer stays for the rest of the
game (`Artemisia_Building_Destroyed`, `a2lm10_flyingjackal.a2lm10_mission:f_15_Out`;
`a2lm10_flyingjackal.a2lm10_wagerexplosion:DoExplosion`).

### `A2LM11` BridgeTooFar: Yabek's barge (UFLL)

**The game states**

- **Briefing.** Voorhees: "Going after an arms dealer today. Bit of a specialty of yours, as I recall."
  (`4560779`, `4560784`); "The target's name is 'Yabek.' He's a Jordanian. … a barge on the Noga River. APR
  territory." (`4560855`). Mbantuwe: "Does his business on a barge, so he can keep moving." (`4560867`);
  "East Branch of the Noga River now. Under the rail crossing." (`4560872`) (`scene ml11_se01_*`).
- **Base job.** Kill Yabek on his barge.
- **Buddy's plan.** A gravity bomb salvaged from a wrecked Soviet HIND needs a fuze from the crash
  site. The buddy drives the bomb onto the rail bridge above the barge, the player primes it, and the
  span drops on Yabek (`Objectives/A2LM11_03` to `A2LM11_05`). Each buddy has his own grudge, for
  example: Yabek backed out of a heroin buy (`4934298`, Frank Bilders); "He's no good. Better off
  dead." (`4933846`, Nasreen Davar).

**How it turns out.** Yabek dies either way. On the plan, the rail span over the Noga stays down for
the rest of the game: the intact-bridge layer is switched off and the wreck layers on, and none is
restored (`a2lm11_abridgetoofar.a2lm11_bridgeexplosion`). The buddy going down on the bridge before
the bomb is primed cancels the plan (`a2lm11_abridgetoofar.a2lm11_giveitempawnbrief:f_1_BuddyDown`).

### `A2LM12` BunkerBuster: the diverted air drop (UFLL)

**The game states**

- **Briefing.** The two share a rare bottle. Voorhees: "Lucky for you all of my old mates have
  buggered off." (`4560954`). The target: "He's a shop teacher named Seth Uniya. Training the APR in
  improvised bombs." (`4561003`, Voorhees); "He thinks he's safe because he is a teacher. I seriously
  doubt he will survive the week." (`4561030`, Mbantuwe) (`scene ml12_se01_*`).
- **Base job.** Kill Uniya in his classroom at the polytechnic.
- **Buddy's plan.** Uniya and a pilot divert relief air drops, using the cell towers as beacons, and
  sell the aid: "They redirect aid shipments. The pilot drops it here, the teacher sells it at a
  profit." (`4934074`, Hakim Echebbi). The player steals the tower map, makes Uniya phone in new
  coordinates, then kills him (`Objectives/A2LM12_03`, `A2LM12_04`).
  - Uniya: "I'm not the one you're looking for… I am an innocent man…" (`4910909`); "OK! It's done. -
    I see you later. OK?" (`5128833`) (`scene ml12_se05_*`).

**How it turns out.** Uniya dies either way. On the plan, a C-130 flies over and the wind carries the
drop onto the airport runway, onto an APR garrison: "The wind pushed the drop right onto the fucking
runway!" (`4934798`, Frank Bilders; `Objectives/A2LM12_05`; `a2lm12_bunkerbuster.a2lm12_planeflyby`).
Crates stay on the airfield afterwards, on both endings (`PersistantAirportEntities`,
`a2lm12_bunkerbuster.a2lm12_mission`, close-out).

## `A2SM08` WarlordAssass: the peace letter

Story mission; 40 diamonds, paid on acceptance; graphs `a2sm08_warlordassassination.*`. Given by the
Act 2 winner's warlord in his Port Selao office (`master_world2.world2:f_200_A_eq_B`).

**The game states**

- **The overheard argument.** The lieutenants oppose the peace. Greaves: "Easy for you to say. You're
  sitting in the first seat. - Now I'm warning you: don't even THINK about shutting me out."
  (`4560185`); "This is the worst idea I've heard in a long time." (`4560193`) (Greaves;
  `scene sm08_se01apr_helper_convo`). Voorhees: "when the big dogs join forces, they're usually doing
  it to take out the REST of the pack." (`4561108`, Voorhees).
- **The letter.** Tambossa: "I've been in contact with Mbantuwe. We have decided that it's time to bring
  an end to this conflict" (`4560243`); "Here. I want you to take this to Mbantuwe. He is at the Seko
  Brewery. Tell him I accept his terms." (`4560255`); "In exchange, he will give you a diamond attaché
  case." (`4560267`) (Tambossa; `scene sm08_se01apr_boss_briefing`). Mbantuwe sends the player to
  Tambossa at the Dogon Sediko the same way: "A symbol of good faith. Bring it back here." (`4561194`,
  Mbantuwe). The lieutenant hands over the file: "Gotta ask yourself who benefits from this deal,
  china." (`4561211`, Voorhees).
- **The buddy's warning.** The best buddy meets the player and proposes taking the diamonds and
  flying out: "This cease-fire the warlords are planning? - It's a death sentence for independent
  operators like you and I." (`4898343`, Hakim Echebbi); "We've arranged for a pilot to exfil us out
  of this shithole, but he won't do anything unless we pay him." (`4898399`, Hakim Echebbi).
- **The bodies.** At the meeting place the other warlord and his guards lie dead, and the journal says
  so: "Mbantuwe's sudden death isn't gonna help me or the peace deal… It's starting to look like
  PRIMARY_BUDDY_NAME has the right idea" (`Objectives/A2SM08_APR_03`).
- **The Jackal.** Reaching for the case brings the Jackal: "Don't make a move, or I'll cut your
  throat." (`4577669`); "If you're here to help rubber-stamp that cease-fire, you're too late."
  (`4577682`); "They're going to think YOU did this… It means the war will start all over again."
  (`4577714`); "Wake up. I used to BE you." (`4577731`); "What have you got there? Diamonds?"
  (`4577771`) (Jackal; `scene sm08_se03_apr_jackc_*`). 9.2 seconds into his exit he knocks the player
  out (`SendPierceStim`, `a2sm08_warlordassassination.a2sm08_mission_forblue:f_59_TimeElapsed`).

**How it turns out.** One ending: the knock-out, after which the mission completes
(`a2sm08_warlordassassination.a2sm08_mission_forblue:f_66_Out` onward).
- **The buddy's plan has no consequence.** The talk plays, but its graph's output is wired to nothing:
  no objective, no airfield, no check of where the player goes
  (`a2sm08_warlordassassination.a2sm08_mission_forblue`, box 12 `Out`).
- **The case.** No item is given; the diamonds were paid at acceptance. The Jackal later speaks of
  "another briefcase full of diamonds" (`4920105`, Jackal).

**Implied**

- The Jackal keeps the attaché case.

## `A2SM09` Prison: the cell and the escape

Story mission; 0 diamonds; achievement "escapetorture"; graph `a2sm09_prison.a2sm09_mission`. Starts
at once after `A2SM08` (`master_world2.world2:f_205_A_eq_B`).

**The game states**

1. **The cell.** The player wakes in a prison cell with no weapons (`ManageInventory.RemoveAllWeapon`).
   The journal: "It's all falling apart, thanks to The Jackal. He murdered Mbantuwe. And when I showed
   up, he sucker-punched me and let me take the fall." (`Objectives/A2SM09_APR_01`).
2. **The buddy in the next cell.** His best buddy is in the next cell, working a brick loose. The
   buddy blames him for the diamonds: "Why in god's name didn't you bring the diamonds?" (`4934566`,
   Hakim Echebbi). The pilot left them "right there on the tarmac" (`4899983`, Frank Bilders), then the
   soldiers came, and "They're blaming the assassination on all the foreigners" (`4934570`, Hakim
   Echebbi).
3. **The guards take him.** Guards come and take the buddy away, and sounds from another room follow
   (`a2sm09_prison.a2sm09_mission:f_62_Out`).
4. **The escape.** The player pushes through the loosened wall and picks up a machete. The script sets
   his malaria pills to unlimited (`SetMalaria.SetUnlimitedPills`, `f_86_Use` to `f_95_Finished`).
5. **The buddy again.** He finds the buddy kneeling elsewhere in the prison, beaten: "Go. I'm too weak
   to run." (`4898913`, Nasreen Davar); "I'll get a souvenir for ya, mate! An ear or maybe a couple of
   teeth!" (`4899881`, Frank Bilders). Two guards in a hall chat about a Friday and the tickets they
   have bought (`4561451`, prison guard).

**How it turns out.**
- **Talking to the buddy** counts as saving him (`MissionCompleted("SAVE_BUDDY")`), and he leaves alive
  (`f_134_Greet_finished`, `f_117_*`).
- **Walking past him** kills him as betrayed (`BuddyDied.Betrayed`, `f_118_Is_not_nil`).
- Either way, leaving the prison completes the mission, and the phone rings for `A3SM11` (see
  [Act 3](./act-3.md)).

**Implied**

- The sounds from the other room are the buddy being tortured: the achievement is "escapetorture",
  and he says "Do you KNOW... what they will do to me in here...?" (`4899151`, Nasreen Davar).
- No script places the other buddies at the airfield or captures them. "I don't know if any of the
  other guys made it." (`4899375`, Andre Hyppolite) is the only account.
