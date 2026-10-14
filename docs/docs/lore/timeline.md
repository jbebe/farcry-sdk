---
sidebar_position: 4
---

# Timeline

What happened before the game, what happens during it, and the few dates the game gives.
Citations follow [Sources and citations](./sources.md). Many story strings are templates filled in
by the player's choices (`WINNING_FACTION_BOSS` and so on); the steps below name both outcomes where
they differ.

## Dates and durations

**The game states**

- **The only calendar dates are the Jackal tapes' recording dates**, all in 2008:

  | Recorded | Tapes |
  | --- | --- |
  | 17/09/2008 | 00 Infamy, 04 Home, 08 The Humanist |
  | 20/09/2008 | 02 Circulation, 06 Career Counseling |
  | 24/09/2008 | 03 Economics, 05 Rat-Catcher |
  | 2/10/2008 | 01 Smuggling, 07 Stability |
  | 5/10/2008 | 11 Necessary Evil, 15 No Sense |
  | 13/10/2008 | 12 Lion's Share, 14 Gutshot |
  | 15/10/2008 | 09 Stealing Boots |
  | 19/10/2008 | 16 Numbers |
  | 30/10/2008 | 10 Gluten-Free, 13 Unrecognizable ("Last words of the Jackal. Interview over?") |

  Source: `JackalTapes/A1JT00_DESC`…`A2JT16_DESC`. Tapes 00–08 are found in Leboa-Sako (`A1`), 09–16
  in Bowa-Seko (`A2`).
- **Durations.**
  - The war "has been going on for a long time" (`4558579`, Gakumba).
  - Reuben has been "interviewing people for months now" (`4577477`, Reuben).
  - Refugees have waited "for weeks" for their papers (`4890908`, Underground refugee).
  - The purges come "After weeks of killing each other off" (`LoadScreens/CuSyn_13`).
  - A rumour gives the Jackal "three months to live" (`4557923`).

**Implied**

- The story runs over weeks to a few months in late 2008. Every tape found in the north was recorded
  between 17 September and 2 October, every tape found in the south between 5 and 30 October. The
  recording dates therefore follow the player's road through the war: Reuben kept interviewing the
  Jackal while it went on.
- Reuben says of a taped interviewee "he died a few months ago. These are his famous last words"
  (`4911521`, Reuben, `A2JT03_SE01`). That does not fit the Jackal, who is alive, so either the line
  means someone else or the tapes are older than their labels say. Left open. The last-dated tape,
  as fans transcribed it, has the Jackal finding Reuben's truck burned and not knowing whether Reuben
  is alive (see [The Jackal tapes](./jackal-tapes.md); community-reported).
- The player may lie ill in the hotel for some time before the Jackal comes. The opening's last
  designer label reads "EVENT 160 10 days later", a time skip no shipped text shows
  (`openingsequence.taxiride:f_280_Out`).

## Before the game

**The game states**

- **There was a King.** "You know this FUBAR country used to have a king?" (`4558420`, Carbonell).
  - "The hombre got a boatload of money from Europe, back in the day. And then he took off."
    (`4558425`, Carbonell).
  - "He abandoned it a long time ago." (`4932774`, Flora Guillen).
  - "I mean he gutted it, man! Took it for everything it's worth!" (`4884388`, Marty Alencar).
- **The country was once rich.** "You know, it looks like poor African country NOW. But this place
  used to have lots of money." (`4933162`, Josip Idromeno).
- **A government fell.** Quarbani Singh did VIP protection "in the city" during "the civil war here",
  and "Bungled the evacuation of his clients after collapse of the government"
  (`Buddies/QUARBANI_SINGH_DESCRIPTION_1`, `_3`).
- **The factions' war is old and settled into habit.** "Both sides tolerate the conflict, because
  we understand it." (`4558579`, Gakumba); "Both sides are used to it!" (`4558660`, Kouassi).
- **Arms were embargoed, and nobody abroad cared.** "It wasn't so long ago that this was another one
  of those dirty little wars that barely rated a sidebar. There was an arms embargo in place, and
  everyone back home could just cluck their tongues" (`LoadScreens/ChSyn_AA`).
- **Then the Jackal came.** "When The Jackal showed up here with containers full of AKs and RPGs -
  That set off some alarm bells somewhere." (`LoadScreens/MiSyn_AA`). The player's orders blame him
  for "the recent influx of weapons into the country, in clear violation of the Joint Signatory
  Framework" (`4577324`, Jackal).
- **A first hunter failed.** "You're not the first to be sent after the Jackal - there was another man
  before you." (`5124037`, handler?).
- **Foreigners were leaving.** At the airport: "That's probably the last one out of this country.
  Full of rich people. Only money and connections get you out now." (`5160475`, taxi driver).
- **A cease-fire was meant to hold.** "There was supposed to be cease-fire, but that's all out the
  window now." (`4547658`, Kankaras).

**Implied**

- The order is: the King's flight with foreign money; civil war and the collapse of the government;
  the long UFLL–APR war under an embargo; the Jackal's arms; the Framework's hired killers. Nothing
  dates any of it.

## The story

### Act 1: Leboa-Sako

1. **Arrival.** The player lands at the airport and takes a taxi to Pala, which "only has one hotel
   still working" (`5160146`, taxi driver). On the way he is told "Everyone's trying to find a way
   out of the country" (`5160098`).
2. **The hotel.** The Jackal enters the player's room, reads out his orders and leaves him alive:
   "I'm still breathing. And YOU'RE the one with malaria." (`4577353`, Jackal;
   `LoadScreens/CuSyn_00`).
3. **Pala erupts.** "There's fighting all over the streets." (`Objectives/A1SM01_02`). The player
   wakes in a faction lieutenant's camp. Each of the four lieutenants blames him for men killed "at
   the hotel" (`4547786`, Quiepo; `4547638`, Kankaras).
4. **First work.** The lieutenant who took him in (`FIRST_LT`) sends him to the rival lieutenant's
   camp. There he frees a foreign captive, his first buddy (`Objectives/A1BU01_01`;
   `LoadScreens/ChSyn_01`).
5. **Medicine.**
   - At Mike's bar the player meets Reuben, who sends him to Father Maliya's church for pills
     (`4577402`, Reuben).
   - The priest asks for help in return: "there are folks trapped in Leboa-Sako who need his help more
     than I do" (`LoadScreens/MiSyn_A1GM00`).
6. **Both factions' jobs.**
   - `FIRST_LT` introduces the player to his boss, Gakumba or Kouassi, who "promised me information
     about the Jackal" (`LoadScreens/CuSyn_02`).
   - The rival lieutenant poaches him for the other boss (`LoadScreens/CuSyn_03`).
   - The jobs: a police chief, gas tankers, a motor pool, foreign commandos, a greenhouse, the King's
     gold (`Objectives/A1LM01_01`–`A1LM06_01`).
7. **Goka Falls.**
   - One boss sends the player to kill the other "so that WINNING_FACTION_BOSS can seize control of
     Leboa-Sako" (`LoadScreens/MiSyn_A1SM02`).
   - Then he is set up: "Now APR gunmen are coming out of the woodwork." (`Objectives/A1SM02_APR_02`).
8. **The purge of Pala.**
   - The winning faction launches "a massive offensive in the northern district", with the player
     its "first target" (`4911034`, Reuben). Foreigners are "declared spies" (`4911066`, Reuben).
   - The player can save Mike's bar or the church, not both (`Objectives/A1SM03_02`). Either way he is
     shot down and knocked out, and nothing in the scripts destroys the place he did not defend
     (`a1sm03_defensereversal.mikesassault`, `churchassault`).
   - Reuben leaves "to Mosate-Selao, in the south" (`4911115`, Reuben).
9. **Left for dead.** "Leboa-Sako fell to the APR, and me with it." — or to the UFLL. "They're
   hauling the dead out to some mass grave in the desert… looks like I'm the only one in this truck
   with a pulse" (`Objectives/A2SM04_APR_01`).
10. **The sandstorm.** The Jackal finds him in the desert and drags him to shelter: "I thought you'd
    be dead by now." (`4577582`, Jackal). Then the losing faction's column, "proceeding south"
    under Greaves or Voorhees, takes him in (`4558817`; `LoadScreens/MiSyn_A2SM05`).

### Act 2: Bowa-Seko

11. **Revenge.** The column's leader sends the player back north, armed with only a machete, to kill
    the winning boss at his victory rally: "Standing on a truck. Wide open." (`4973102`, Greaves;
    `Objectives/A2SM05_APR_01`). By now the whole northern map is the winner's
    (`a2sm05_reprisal1.a2sm05_mission_forblue:In`, `ChangeMapArmy`).
12. **Sefapane and the southern cease-fire.**
    - The player meets his new patron at Sefapane, a town "split down the middle between the
      factions" (`Objectives/A2SM06_APR_01`).
    - "The fall of Leboa-Sako brought a cease-fire in the south." (`Objectives/A2SM06_APR_02`).
13. **The false flag.**
    - The patron has the Jackal's weapons barge sailed from his island into Port Selao under the
      other faction's flag (`Objectives/A2SM06_APR_02`).
    - The player warns Doctor Obua at the clinic: "Even this cease-fire was too much to ask for."
      (`4939226`, Doctor Obua).
    - Port Selao's cease-fire collapses: "I had soldiers from both factions firing on me."
      (`4897741`, Hakim Echebbi).
    - The barge's skipper, a stranger who becomes the player's buddy, scuttles it at the old marina and
      settles there (`4897680`, Andre Hyppolite). After that the capital is a cease-fire zone again and
      the faction HQs open for work (`a2sm07_jacksbuddy.a2sm07_mission`, close-out).
14. **The warlords' jobs.** The player now works for Tambossa and Mbantuwe:
    - silencing Liberation Radio;
    - rupturing the water pipeline;
    - destroying nitrous oxide at a dental clinic;
    - the artemisia kilns;
    - the gun-runner Yabek;
    - the bomb teacher Uniya.

    (`Objectives/A2LM07_01`–`A2LM12_01`.) Where the player follows his buddy's plan instead, some of
    them leave lasting marks: the TaeMoCo mine flooded, the APR's artemisia store burned, the rail
    bridge over the Noga dropped (see [Act 2](./missions/act-2.md)).
15. **The peace.**
    - "This battle of ours is going to end. Oliver Tambossa and I have talked, and we agree."
      (`4561167`, Mbantuwe).
    - The player carries one warlord's acceptance to the other and is to bring back "a 'good-faith'
      tribute of diamonds" (`Objectives/A2SM08_APR_02`).
    - His buddies want those diamonds for an evacuation flight: "This cease-fire the warlords are
      planning? - It's a death sentence for independent operators like you and I." (`4898343`, Hakim
      Echebbi).
16. **The murder.** The Jackal is there first. "He murdered Mbantuwe. And when I showed up, he
    sucker-punched me and let me take the fall." (`Objectives/A2SM09_APR_01`, or Tambossa in the
    UFLL version). In his words: "They're going to think YOU did this… It means the war will start
    all over again." (`4577714`, Jackal).
17. **Prison.**
    - The player wakes "in some cell, god knows where" (`Objectives/A2SM09_APR_01`).
    - The buddies are caught too: "The factions are blaming the assassination on us… all the
      foreigners" (`4899395`, Andre Hyppolite). Their pilot "left us right there on the tarmac"
      (`4899983`, Frank Bilders).
    - His weapons are gone. His best buddy, in the next cell, is taken away by the guards, and the
      player finds him later, beaten (`a2sm09_prison.a2sm09_mission:f_62_Out`; `4898913`, Nasreen
      Davar). Walking past him without a word kills him (`f_118_Is_not_nil`).
    - He breaks out through the wall his buddy loosened, with a machete (`LoadScreens/ChSyn_12`;
      `a2sm09_prison.a2sm09_mission:f_86_Use`).

### Act 3: the endgame

18. **The second warlord.** "He's running the APR now that Tambossa is dead - And he's sending me to
    eliminate Mbantuwe once and for all." (`Objectives/A3SM11_APR_02`, Greaves; Voorhees in the UFLL
    version).
19. **The purges.**
    - `FIRST_LT` is "keeping Tambossa's seat warm" and wants Greaves and his aide killed at Sepoko
      (`Objectives/A3SM12_APR_02`).
    - Then, at the Marina bar, `SECOND_LT` asks the same of the other faction (`Objectives/A3SM13_APR_02`).
    - At each site, the man marked to die offers to give up his employer instead (`Objectives/A3SM12_APR_03`).
    - Whichever of a faction's three men the player leaves alive leads it to the end
      (`common_customboxes.setfinalwarlord`; see [Act 3](./missions/act-3.md#who-is-who-in-act-3)).
20. **The truce and the airport.**
    - "The APR and UFLL have called a truce. Unity at last, eh?" (`4911264`, Reuben).
    - Reuben is held at the airport with the other journalists (`4934582`, Reuben).
    - He reports the Jackal's last moves: the vendor attacked, the TNT taken, and "He's going to
      evacuate this entire country. Every civilian." (`4911306`, Reuben).
21. **The prison again.** The Jackal has cleared the prison the player escaped and turned it into
    "a way-station for evacuating all of the remaining civilians over the border"
    (`LoadScreens/MiSyn_A3SM15`). He sends the player for two jobs: the arms merchant's diamonds at a
    landing zone, and the two united leaders at their bivouac (`4920105`, `4969696`, Jackal).
22. **The betrayal.**
    - At the landing zone the arms merchant is already dead. The best buddy and the buddies the
      player betrayed have the diamonds and have made their own deal: "We made our deal. He's on his
      own." (`5106412`, Nasreen Davar; `a3sm15_buddiesbetrayalfinalbattle.a3sm15_buddiesfight`).
    - Reuben had warned of this: "They've become bloody collaborators." (`4911316`, Reuben).
    - "It's done. Everyone's dead. Enemies… friends. Everyone except me and The Jackal."
      (`Objectives/A3SM15_02`).
23. **The pass.**
    - One man must carry the diamonds to the Border Patrol camp. "Then, when the handoff's complete,
      that guy - you, or me - is going to put a bullet into his own head." (`4973291`, `4973210`,
      Jackal).
    - The other must set off the dynamite above the canyon by hand, with a car battery: "And it'll
      kill the poor bastard that takes the job." (`4577966`, `4973018`, Jackal).
    - The player chooses: "Either pick up the battery or the briefcase." (`Objectives/A3SM15_03_BBM_01`).
    - The commandant takes the bribe: "I think we can reach an accommodation." (`5161548`, Border
      Patrol Commandant).
    - Both choices end in the same closing scene: civilians crossing the border, Reuben with a camera,
      and the pass blowing (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_closingscene`). Neither shows the
      player die.
24. **Afterwards.** "Following the exodus, over two million refugees were displaced to neighboring
    countries." An interim government of the factions' remnants fails; "Anarchy rules to this day."
    The Jackal's body "has never been found." (`Coda/Para1`, `Para2`, `Para4`).

**Implied**

- **The Jackal's turn.**
  - In Act 1 he sells cheaply to both sides (`4557681`). In Act 2 he gives weapons away (`4560002`,
    Greaves), then kills a peacemaker to restart the war (`4577714`).
  - In Act 3 he empties the country of civilians: "We're have to isolate this disease, so we can
    destroy it." (`4920067`, Jackal).
  - Read together, his plan from the peace meeting on is to keep the factions fighting until they
    destroy each other, with the civilians moved out of the way.
- **Whether his plan works is left open.** The factions are wrecked, but the closing text says
  "Anarchy rules to this day" and the Jackal's death is unconfirmed (`Coda/Para2`, `Para4`).
- **The player's patrons all end up dead**, whichever faction he serves: the Leboa-Sako bosses at
  steps 7 and 11, the warlords at 16 and 18, the lieutenants at 19 and 22. Every step that kills one
  is a job he took.
