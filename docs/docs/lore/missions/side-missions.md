---
sidebar_position: 5
---

# Side missions

Everything the master graphs run alongside the story: the buddies' side quests, the Underground's
medicine jobs, the arms vendor's convoys, the cell-tower contracts, the Jackal tapes and the
predecessor's trail. Citations follow [Sources and citations](../sources.md); script citations name
the file in `domino\user\` (the side systems sit in `domino\user\sidemissions\` and
`domino\user\partnerspecialmissions\`) and the handler.

## Buddy side quests `BSQ01`–`BSQ26`

**The game states**

- **Who offers them.** Every buddy has two, offered at Mike's in Leboa-Sako or at the Marina in
  Bowa-Seko. He offers the second only after the first is done. Marty Alencar, the one buddy in both
  worlds, has four (`sidemissions.mikesplace_bsqmanager_w1`, `_w2`).
- **When.** The bar is in one of three modes. Between faction jobs it offers side quests; while a job is
  open the best buddy is away; during story missions nobody is there
  (`sidemissions.mikesplace_bsqmanager_w1`; `master_world1.world1:f_222_Out`;
  `master_world2.world2:f_140_Out`). New offers appear after the player has been away from the bar for
  ten seconds (`sidemissions.mikesplace_pawnbriefselection_timer`).
- **How they run.** The buddy briefs, the player does one thing (destroy, fetch or kill), and the buddy
  debriefs back at the bar. None has a failure path. Accepting any other job cancels the side quest,
  which can be taken up again later (`common_buddysidequests.bsq_activate_and_cancel`).
- **No diamonds.** The record table gives each a small reputation bonus (`infbonus`), and the debrief
  tells the engine the side quest succeeded (`SetSidequestMissionState.Succeeded`).

| Id | Buddy | What he asks |
| --- | --- | --- |
| `BSQ01` | Josip Idromeno | blow up a propane tank the UFLL means to drive into a border checkpoint: "lots of children on other side of border" (`4906506`) |
| `BSQ02` | Michele Dachss | fetch his files from a train rebels raided: "My evidence against the factions" (`4907208`) |
| `BSQ03` | Flora Guillen | destroy guns hidden at a ranch the APR has seized (`4905724`) |
| `BSQ04` | Warren Clyde | burn the fuel at Shwasana: "those gas-station assholes are taking the money … giving it to the factions" (`4906028`) |
| `BSQ05` | Paul Ferenc | fetch documents from Commander Davenport's wiped-out supply crew at the Legion Fort |
| `BSQ06` | Marty Alencar | destroy cargo Commander Alessi's APR mercs took and never paid for |
| `BSQ07` | Warren Clyde | steal a sack of khat from the OGC greenhouses |
| `BSQ08` | Quarbani Singh | fetch a prototype case from a police-station garage: "The factions are experimenting" (`4906904`) |
| `BSQ09` | Marty Alencar | destroy the junkyard medic's fake medicine, which was ointment for his rash (`4903462`) |
| `BSQ10` | Paul Ferenc | steal the APR's recipe for the defoliant "Agent Yellow" from the chemical dump |
| `BSQ11` | Michele Dachss | kill two APR men selling "Hazelnuts", a drug "designed exclusively for children" (`4907428`) |
| `BSQ12` | Josip Idromeno | kill the car dealer hiding in Mokuba |
| `BSQ13` | Quarbani Singh | dig up his lockbox at a desert oasis |
| `BSQ14` | Nasreen Davar | kill an APR official snooping at the Dogon village: "There are things there that I don't want him to find." (`4907922`) |
| `BSQ15` | Flora Guillen | stop a convoy of furniture for Mbantuwe's mansion: "His people starve in the street, and he buys gold-plated dishes." (`4905744`) |
| `BSQ16` | Hakim Echebbi | kill the thug who mugged him (`4908554`) |
| `BSQ17` | Andre Hyppolite | fetch accounts from the pump house: "I have a feeling I'm being cheated." (`4908450`) |
| `BSQ18` | Xianyong Bai | destroy a rival's passports at the tourist resort: "Everybody wants to get the hell out of here!" (`4909162`) |
| `BSQ19` | Frank Bilders | destroy a radio broadcasting his description |
| `BSQ20` | Hakim Echebbi | steal Greaves's meeting notes from his bedroom at the APR post in Sefapane, to sell to Mbantuwe (`4908604`) |
| `BSQ21` | Marty Alencar | kill a rival arms smuggler at the UFLL post in Sefapane; the post turns hostile |
| `BSQ22` | Xianyong Bai | fetch pottery Voorhees also wants: "Voorhees, big fat pig!" (`4909172`) |
| `BSQ23` | Frank Bilders | blow up the polytechnic workshop of a mate who cut him out |
| `BSQ24` | Nasreen Davar | kill a man at the airport: "Drive by and blow his head off!" (`4908092`) |
| `BSQ25` | Andre Hyppolite | fetch the trophy box of the man who killed Tambossa's brother, for Mbantuwe |
| `BSQ26` | Marty Alencar | kill the TaeMoCo mine's chief for Mbantuwe: "Me. After what I did to his girl." (`4907732`) |

## The Underground's medicine jobs

**The game states**

- **Who gives them.** Father Maliya in Pala and Doctor Obua in Port Selao, or any Underground operative
  already met (`sidemissions.grin_missions:f_279_Out`). They have no mission record and complete under
  the id `GRIN` (`sidemissions.grin_missions:f_57_Out`).
- **When.** In Leboa-Sako after the 4th, 6th and 8th completed story mission or faction job; in
  Bowa-Seko after the 3rd, 5th, 7th and 9th (`sidemissions.grin_missions:f_144_Output_*`,
  `f_155_Out`). The journal: "I'm running low on malaria medicine. I'd better think about finding more."
  (`Objectives/A0GM00_01`).
- **The cells.** Four per world, each a shop with an operative and a refugee family hiding in the back:
  a veterinarian, a general store, an undertaker and a windmill shop around Pala; a general store, a
  phone-card shop, a hardware store and a tobacconist around Port Selao
  (`Objectives/A1GM01_01` to `A2GM04_01`). Each is offered once, in random order.
- **A job.** The briefer hands over travel papers: "Take these travel papers to him." (`5122130`, Father
  Maliya). APR mercenaries hold the cell; the player clears them. The operative thanks him: "Thank you
  so much. Those men are a plague." (`4889879`, operative). The refugee trades medicine for the papers:
  "You have the fever. My daughter too. We will share our medicine. You have travel documents?"
  (`4890043`, refugee). The player gets unlimited pills, and the cell becomes an unlocked Underground
  location (`sidemissions.grin_missions:f_57_Out` to `f_63_Out`).
- **Infamy.** Once the engine says the player can no longer get medicine, he gets one last job, and
  every later offer is refused (`PlayerCanAccessMedicine`, `sidemissions.grin_missions:f_197_No`,
  `f_184_A_is_True`). The briefers' lines for an infamous player: "With these travel papers, he'll turn a
  blind eye to the horrors you've committed." (`5123551`, operative); "Take these and go. My associates
  want nothing more to do with you." (`4890290`, Doctor Obua).
- **Malaria blackouts.** After the first completed mission, a malaria blackout wakes the player at
  Maliya's or Obua's with a few pills: "You must treat your fever with more care, sir. I can spare these,
  but you will need more soon." (`4889445`, Father Maliya) (`sidemissions.grin_missions:f_111_Out` to
  `f_102_Out`).

**Implied**

- Nothing handles the death of an operative or a refugee. If either died, the job could never finish
  and no further job would be offered in that world. Whether they can die is not in the scripts.

## The arms vendor's convoys `A1CV01`–`A1CV04`, `A2CV05`–`A2CV08`

**The game states**

- **The jobs.** The weapon-shop vendor sends the player to destroy a rival's convoy, four per world, in
  random order. "My competition's bringing in a shipment of shitty AKs and the usual low-end trash. From
  the northwest..." (`4913005`); "This Ukrainian's trying to move in on my business… Bad product, none
  of it clean." (`4913015`); "These bastards from Moldova. They think they can walk into another man's
  home and steal his business." (`4913179`) (arms vendor; `sidemissions.convoymissions.selectrandommission`).
- **The pay** is new stock, in the order the convoys are done, not by convoy
  (`sidemissions.convoymissions.unlockweapons`):

  | Convoy | Leboa-Sako | Bowa-Seko |
  | --- | --- | --- |
  | 1st | Star .45, flare gun, 6P9 | M249, AS50 |
  | 2nd | AK-47, MAC-10, RPG-7 | M16, mortar |
  | 3rd | SPAS-12, MP5, LPO-50 | USAS-12, Carl Gustaf |
  | 4th | Dragunov, M79, dart rifle | MGL-140 |

- **Failing** lets the convoy through; the vendor offers the same one again
  (`master_world1.world1:f_98_Out`).

## The cell-tower contracts `A1AS01`–`A1AS06`, `A2AS01`–`A2AS06`

**The game states**

- **The call.** Using a cell tower's panel brings a call: "Listen, very carefully. There is an available
  target of opportunity. Proceed to the objective marked on your map. Terminate the target. Standard
  payment -- in rough diamonds -- will be transferred to your account on completion." (`5175655`, the
  voice is never named) (`sidemissions.assassinationcelltowerinteracted:f_26_Out`).
- **The targets.** Six per world, drawn in random order. One per world rides in a convoy; the rest are on
  foot (`sidemissions.setassassinationvariables`).
- **The pay.** 10 diamonds in Leboa-Sako, 15 in Bowa-Seko (`mission_records.xml`,
  `AssassinationRewardWorld1`, `World2`).
- **How they end.** A foot target always dies; the script has no way for him to escape. A convoy target
  can get away, and that contract is lost (`sidemissions.assassination_missions:f_41_Failure`).

## The Jackal tapes

**The game states**

- **Sixteen tapes**, eight per world. Each one found adds one to a single count
  (`sidemissions.carvertapes_missions:f_69_Out`).
- **Reuben listens.** At every fourth tape he waits at Mike's or the Marina to hear them
  (`sidemissions.carvertapes_missions:f_34_*`). "Information is a weapon here. It's more useful than guns
  sometimes." (`4911492`); "something is changing ... something big is about to happen" (`4911502`)
  (Reuben).
- **His visits end.** In Leboa-Sako when Goka Falls ends; in Bowa-Seko when the first purge ends
  (`master_world1.world1:f_168_Out`; `master_world2.world2:f_153_Out`).
- **The payoff** is his information at the airport in `A3SM14`, and what reaches it is limited (see
  [Act 3](./act-3.md#a3sm14-heartdarkness-reuben-at-the-airport)).

## The predecessor's trail

Graphs `partnerspecialmissions.*`; layers `Missions/VendorMissions/A1VM01`–`A3VM09`.

**The game states**

- **Locked, not cut.** The engine runs these only after a password is entered on the main menu's
  Additional Content page: "You have successfully unlocked the missions."
  (`MainMenu/EXCLUSIVE_CONTENT_B2B_SUCCESS`; `partnerspecialmissions.psm_unlocker:f_4_Out_1`). The flag is
  stored in the player profile, not the save. *RE-verified on the Linux server binary only:*
  `CFCXExclusiveContentMenuPage::ValidatePassword` checks a built-in list of twenty codes and sets
  `CGameConfig.PartnerMissionUnlocked`.
- **The first call.** At Pala's hub: "You're not the first to be sent after the Jackal - there was
  another man before you. He went silent - presumed dead. But it now appears he might have been
  compromised. … Locate the missing man and eliminate him." (`5124037`, voice unnamed)
  (`partnerspecialmissions.mission_01:f_3_A_is_False`).
- **The informants.** Three message drops per world lead to three informants, each found dead with one of
  the predecessor's tapes: the Moroccan, the Georgian and the Bolivian in the north; the American, the
  Frenchman and the Brit in the south (`JackalTapes/W1V1M1_DESC` to `W2V3M1_DESC`). Bowa-Seko replays the order
  the player took in Leboa-Sako (`partnerspecialmissions.psm_world_1`, `psm_world_2`).
- **The invitation.** "I hear you are looking for me, and for my friend, the Jackal. You can find us on
  Sehlakalase Island. Middle of Lake Segolo. Whenever you're ready." (`5123973`)
  (`partnerspecialmissions.psm_world_2:f_25_A_is_True`).
- **The end.** The predecessor lies dead with a Desert Eagle and a last tape, "The Bullet": "Predecessor's
  last tape. Found on his corpse. Suicide." (`JackalTapes/C0V0M4_DESC`;
  `partnerspecialmissions.mission_09`). The order to eliminate him is never carried out by the player.

**Implied**

- The invitation is the predecessor's own voice: "for me, and for my friend, the Jackal".
- It was once a tape. The unused tape "The Predecessor", "Gives details on where to meet him and the
  Jackal" (`JackalTapes/C0V0M3_DESC`), is never given by any script.
