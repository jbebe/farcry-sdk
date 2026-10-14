---
sidebar_position: 4
---

# Act 3: the purges and the border

The last five story missions: the second warlord, the two purges, the airport and the finale. Act 3
has no faction jobs. Citations follow [Sources and citations](../sources.md); script citations name
the file in `domino\user\` and the handler.

## Who is who in Act 3

**The game states**

- **Your first lieutenant and his rival.** The opening fixes both for the rest of the game. The
  lieutenant whose camp the player woke in is `FIRST_LT`; his rival is the paired lieutenant of the
  other faction: Carbonell and Purefoy, Kankaras and Quiepo
  (`common_customboxes.set_helperfromstory`; `a1bu00_tutorial.a1bu00_storymission:Start_tutorial_with_A1BU01`
  to `_04`).
- **Each faction's three men.** The APR's are Greaves, Purefoy and Quiepo; the UFLL's Voorhees,
  Carbonell and Kankaras. Each purge kills two of a faction's three, and the one left alive leads it
  into the finale (`common_customboxes.setfinalwarlord`). If the script never recorded one, it falls
  back to Greaves or Voorhees (`common_customboxes.getfinalwarlord:f_5_Out`, `f_11_Out`).

| Purge | Given by | Targets | Leader afterwards, if the player kills both targets | Leader afterwards, if he takes the survivor's offer |
| --- | --- | --- | --- | --- |
| `A3SM12` | `FIRST_LT`, in his own faction's HQ | Greaves or Voorhees and the other lieutenant of his faction | `FIRST_LT` | the target he spared |
| `A3SM13` | the rival, at the Marina bar | the other faction's Greaves or Voorhees and its other lieutenant | the rival | the target he spared |

So any APR leader can face any UFLL leader at the end: nine pairings are possible.

## `A3SM11` Reprisal2: the second warlord

Story mission; 40 diamonds, paid on acceptance; graphs `a3sm11_reprisal2.*`. Starts at once after
the prison escape, with a phone call from the lieutenant whose warlord the Jackal killed
(`master_world2.world2:f_204_A_eq_B`).

**The game states**

- **The call.** "Greaves here. I think it's time we had a chat, you and I. Come out to Sefapane, soon
  as you can." (`4970747`, Greaves). Or: "It's Voorhees. Things have changed, eh? Reckon it's time you
  and I had a little talk." (`4969901`, Voorhees).
- **Greaves's offer** (after a UFLL Act 2): "So here's what it's come to. Me, in charge of the whole
  operation." (`4561651`); "My first order of business is, get rid of Mbantuwe. Clean slate and all
  that… You'll find him at the Brewery." (`4561664`) (Greaves; `scene sm11_se01apr_boss_*`).
- **Voorhees's offer** (after an APR Act 2): "Mbantuwe's dead. I'm sure you heard." (`4561553`); "I don't
  much care for you… I don't want to be a bloody warlord." (`4561566`); "…I need a man on the other side
  of the table that I can do business with." (`4561570`); "That means no more Tambossa." (`4561574`)
  (Voorhees; `scene sm11_se01_ufll_*`).
- **The kill.** The warlord waits with his guards and talks first. Mbantuwe: "Get out of my place! …
  This is my house, my country, my continent! You are a scavenger and a trespasser!" (`4909607`,
  Mbantuwe). Tambossa: "Let's clear the air. A misunderstanding. It happens. … You yourself have made
  mistakes. Done things you are not proud of." (`4909566`, Tambossa). He fights if attacked, if the
  player looks away, or 30 seconds after he has spoken (`a3sm11_reprisal2.a3sm11_blue_assassination`).

**How it turns out.** One ending: the warlord dies, and both warlords are now dead
(`a3sm11_reprisal2.a3sm11_mission_forblue`, "MBANTUWE Was Killed").

## `A3SM12` HouseClean1: the first purge

Story mission; 25 diamonds, paid on acceptance; graphs `a3sm12_housecleaning1.*`. Given by
`FIRST_LT`, sitting at the dead warlord's desk in his faction's Port Selao HQ
(`master_world2.world2:f_224_A1_*`).

**The game states**

- **The offer.** Purefoy: "Maybe you came to get a job from that psychopath, Greaves." (`4561817`); "Or
  that backstabber Quiepo. … I'm taking over this outfit." (`4561821`). Carbonell: "You with Voorhees
  now? Me too. I guess." (`4562581`); "I fucking hate that guy." (`4562586`). Kankaras: "Then we'd run
  United Front. We'd be Big Men." (`4562709`). Quiepo: "Guys like us are on the outside, looking in."
  (`4561732`); "You should kill 'em. Both of 'em." (`4561756`).
- **The targets.** Greaves and the other APR lieutenant are at the old Sepoko border crossing; Voorhees
  and the other UFLL lieutenant at the TaeMoCo mine (`Objectives/A3SM12_APR_02`, `A3SM12_UFLL_02`).
- **What they talk about.** At Sepoko the APR men puzzle over the missing villagers: "Everybody says the
  villagers are gone. Not hiding. Just gone." (`4944068`); "Heading for the border, maybe." (`4944076`);
  "Jungle magic" (`4944138`). At TaeMoCo the UFLL men are stripping the mine: bulldozers, steamshovels,
  "We've got to get the whole load on a barge somehow." (`4939814`).
- **The counter-offer.** After the first kill, the survivor bargains: "Quiepo's the man you've got to
  kill… take position on roof across the street… I can order the guards back at HQ. They'll open the
  shutters." (`4939987`, Greaves). The journal: "A tempting offer…" (`Objectives/A3SM12_APR_03`).

**How it turns out.** No branch fails.
- **Kill both at the camp**: the employer becomes his faction's leader
  (`MissionCompleted("HOUSE_CLEANING1_BASE")`).
- **Take the offer**: the player shoots the employer through the opened shutters from across the
  street, then escapes Port Selao. The survivor becomes leader
  (`MissionCompleted("HOUSE_CLEANING1_SUBVERT")`; `a3sm12_housecleaning1.a3sm12_mission_forblue`;
  `Objectives/A3SM12_APR_04`).

## `A3SM13` HouseClean2: the second purge

Story mission; 25 diamonds, paid on acceptance; achievement "killleader"; graphs
`a3sm13_housecleaning2.*`. Given by `FIRST_LT`'s rival, at the Marina bar
(`master_world2.world2:f_223_A1_*`).

**The game states**

- **Reuben's call.** "It's Reuben. Could you meet me at the bar? It's urgent. - I have to warn you, some
  of your old associates are here, so watch your back when you arrive." (`4547558`, Reuben).
- **Reuben's news.** "I paid your friend the lieutenant a lot of money so that you and I could talk."
  (`4911159`); the Jackal's island house has burned and "he's starting his own offensive. And somehow
  he's getting the civilians involved" (`5106733`); "there's been an incident at the airport"
  (`4911215`); "These men are going to take me there…" (`4911220`); "Keep your phone charged."
  (`4911205`) (Reuben).
- **The rival's offer.** He is waiting: "You two lovebirds done talking?" (`4561940`, Purefoy). He wants
  the other faction's leadership dead while he moves into the dead warlord's office
  (`Objectives/A3SM13_APR_02`). The camps and the men in them are the same as in `A3SM12`, for the
  other faction.
- **The counter-offer.** The survivor offers loyal men at HQ who will let the player "walk in the front
  door without checking my guns, and right up the stairs to Tambossa's office"
  (`Objectives/A3SM13_APR_03`). Greaves: "Right in my office. Blow his brains all over the wall"
  (`4944007`).

**How it turns out.** No branch fails. Kill both at the camp and the rival leads the faction; walk into
the HQ armed and kill the rival, and the survivor leads it (`HOUSE_CLEANING2_BASE`,
`a3sm13_housecleaning2.a3sm13_assassination_forblue:f_22_Out`; `HOUSE_CLEANING2_SUBVERT`,
`a3sm13_housecleaning2.a3sm13_subv_assassination_forblue:f_2_Killed`).

## `A3SM14` HeartDarkness: Reuben at the airport

Story mission; 0 diamonds; achievements "heartofdarkness" and "jackalaudio_all"; graph
`a3sm14_heartofdarkeness.a3sm14_mission`.

**The game states**

- **The call.** "It's Reuben. I warned you I might call… They've rounded up all the journalists. They
  say they're going to deport us. Or worse…" (`4934582`, Reuben). The journal: "He's been saying too
  much about the factions' attempts to stop the civilian exodus." (`Objectives/A3SM14_01`).
- **The hangar.** The player fights through to Reuben, who pays in information: "There is only one
  currency I can offer, and that is information." (`4911254`); the factions' truce, an uprising at the
  old prison, and "The Jackal is responsible." (`4911264`) (Reuben).
- **The tapes decide how much he says.** Each four Jackal tapes found add a tier: the Jackal at the
  airport with the vendor's briefcase (`4911274`), the TNT, "six tons" (`4911284`), the evacuation
  through the old prison to Pelo-Laboso with diamonds to bribe the border guards (`4911306`), and with
  all sixteen, that the freelancers became collaborators (`4911316`)
  (`a3sm14_heartofdarkeness.a3sm14_mission:f_79_Out` to `f_120_Out`).
- **His letters.** Reuben stays behind and gives the player letters and photos for his wife Mikela and
  daughter: "If anything happens to me, please make sure they get these." (`5107230`, Reuben).
- **The Jackal at the prison.** "The civilians are gone…" (`4920046`); "An entire country is about to
  disappear." (`4920042`); "They're in the way!… We're have to isolate this disease, so we can destroy
  it." (`4920067`). He gives two jobs: the arms merchant waits at a landing zone "with another briefcase
  full of diamonds… They're MY diamonds. Mine. I need them for the border troops." (`4920105`,
  `4967869`); and the two new faction leaders share one camp: "You could walk right into their bivouac
  and they'd LET you" (`4969696`). "Meet me at the border when you're done." (`4973454`) (Jackal).

**How it turns out.** One ending; Reuben cannot be lost in the script. The script resets the tape count
to zero when the mission starts, before Reuben reads it
(`CarverTapesFound = 0`, `a3sm14_heartofdarkeness.a3sm14_mission:f_3_Out`). Nothing else raises the
count except picking up more tapes.

**Implied**

- In a normal game Reuben therefore gives only his lowest tier, and his higher tiers and this
  mission's "jackalaudio_all" award are out of reach. Not checked in play.

## `A3SM15` BuddyBetrayalFinalBattle: the end

Story mission; 0 diamonds; achievement "resolveconflict"; graphs
`a3sm15_buddiesbetrayalfinalbattle.*`. The cut `A3SM16` "NobleSacrifice" was merged into it (see
[Cut and unused content](./cut-content.md)).

**The game states**

1. **Two jobs, in either order** (`Objectives/A3SM15_01`):
   - **The bivouac.** The two faction leaders the purges left plan the hunt for the refugees together.
     Greaves: "Intel taken off those stragglers near the old crossing puts the slower group here… No
     word back from the team at the prison. Let's assume the worst." (`4972219`, Greaves). Killing both
     completes the job (`KILL_WARLORDS`).
   - **The landing zone.** The arms merchant lies dead beside the briefcase
     (`Corpses.ArmsMerchantCorpse`). The best buddy holds it: "Look! I've got the diamonds."
     (`4934578`, Andre Hyppolite). Then the buddies the player betrayed arrive: "You're wasting time.
     Shoot him." (`4900453`, Andre Hyppolite) / "Shut up. We made our deal. He's on his own." (`5107821`,
     Michele Dachss) The best buddy objects ("Are ye mad?
     He's one of us!", `4900605`, Frank Bilders), then joins them: "I'm sorry, mate. Just business."
     (`5009973`, Frank Bilders). Taking the case completes the job (`BUDDY_BETRAYAL`); no script waits
     for any buddy's death (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_buddiesfight`).
2. **The hut.** The journal: "It's done. Everyone's dead. Enemies… friends." (`Objectives/A3SM15_02`).
   The Jackal waits in a hut past the canyon pass (`scene sm16_se01_noblesacrifice_jackc_*`):
   - "Those civilians are making it to the camps tonight. Every last raggedy one of them." (`4972955`)
   - "I planted some dynamite. Out there. By that overhang on the cliff." (`4577961`)
   - "And it'll kill the poor bastard that takes the job." (`4973018`)
   - "The other guy is going to take those diamonds to the border and use them to bribe the guards…"
     (`4577953`); "you, or me - is going to put a bullet into his own head." (`4973210`)
   - "So which one are you taking? Diamonds or dynamite. It's up to you." (`4577998`) (Jackal)
3. **The choice.** Picking up the battery or the briefcase decides the ending
   (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_finalbriefing:f_47_Use`, `f_41_Use`).

**How it turns out.**
- **The battery** ("Good. You know where to go. I'll be dead in an hour. And so will you." `4578027`;
  "Africa wins again." `4972911`, Jackal). The player walks to the charge under the overhang and
  connects the battery. The screen goes black; no line is spoken
  (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_redemptionending`).
- **The briefcase** ("That gun in there is a good piece. - Never jams." `4578019`; "One shot is all
  you'll need." `4973136`, Jackal). The player hands the case to the border commandant: "I think we can
  reach an accommodation." (`5161548`); "But for the sake of those souls outside, I hope there's more to
  your plan than THIS." (`5161630`) (border commandant). The screen goes black
  (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_survivalending`, `a3sm15_bordercombriefing`).
- **Both endings end the same way.** One closing scene plays for both: civilians cross the border, Reuben
  takes a photograph, and the pass blows. Then the game's end screen
  (`a3sm15_buddiesbetrayalfinalbattle.a3sm15_closingscene`, `PopUpEndOfGame`).
- **No script kills the player.** His death exists only in the Jackal's words: the charge that kills
  whoever sets it, the bullet after the handoff.

**Implied**

- In the briefcase ending the Jackal sets off the charge himself; in the battery ending he carries the
  case to the border. The closing text says the Jackal's "body has never been found" (`Coda/Para4`).
- Only six betrayed buddies can appear at the landing zone: the script fetches ten names and spawns six.
