---
sidebar_position: 5
---

# Places

Where the game is set: the country, its two playable regions, and every named place, from the paper
map's printed labels and from what people say about them. What the world data places at each site
(props, signs, posters, soldiers) is in [Set dressing](./set-dressing/reading-the-world.md#the-gazetteer).
Citations follow [Sources and citations](./sources.md). A map citation names the page the label is printed on:
`w1_c_3` is world 1, row c (rows run north to south), column 3 (columns run west to east).

## The country

**The game states**

- **It is never named.** No string in the English table gives the country a name. People call it
  "this country", and Mbantuwe calls it "my country" (`4909607`, Mbantuwe).
- **It is in Africa.**
  - "Finest cantina in all of Africa!" (`4903290`, Warren Clyde).
  - "You know, it looks like poor African country NOW." (`4933162`, Josip Idromeno).
- **It is inland.** "Hundreds of miles from any kind of ocean… It's a strange place for an Algerian
  navy man to meet his fate." (`5107630`, Frank Bilders).
- **It has neighbours.**
  - One to the west buys its natural gas by rail (`4559291`, Greaves) and its fresh water by
    pipeline (`4559888`, Tambossa).
  - Arms come "over the border north of here" (`4912995`, arms merchant).
  - The refugees end up "displaced to neighboring countries" (`Coda/Para1`).
- **It is hard to get into or out of.**
  - "Nobody can come into country these days." (`4558110`, Kankaras).
  - "Everyone's trying to find a way out of the country." (`5160098`, taxi driver).
- **It has deserts.** Dialogue speaks of desert, wadis and oases (`4906964`, Quarbani Singh;
  `4558461`, Carbonell).

## Regions

**The game states**

- **Leboa-Sako is the north, Act 1.**
  - "Leboa-Sako fell to the APR" (`Objectives/A2SM04_APR_01`).
  - "the northern district" (`4911034`, Reuben).
  - It is spelled "Leboa-Seko" once (`4558791`, Greaves).
- **Bowa-Seko is the south, Act 2.**
  - "Welcome to Bowa-Seko." (`4559146`, Greaves).
  - "Playing the factions in Bowa-Seko turned out to be just as dangerous as it was up north."
    (`LoadScreens/CuSyn_12`).
- **Pelo-Laboso** is named once, as the refugees' way out: "They are coming from Bowa-Seko, moving
  through the old prison and on to Pelo-Laboso. It's rough terrain… no vehicles can pass"
  (`4911306`, Reuben).

**Implied**

- The country has at least three regions with paired names: Leboa-Sako, Bowa-Seko and Pelo-Laboso.
  Pelo-Laboso lies beyond the prison, toward the border the refugees cross. It is off the playable
  map.

## How the two maps relate

**The game states**

- Bowa-Seko is south of Leboa-Sako. Reuben flees "to Mosate-Selao, in the south" (`4911115`,
  Reuben). The retreating column is "proceeding south" (`4558817`, Greaves). The losing faction
  will "meet up with Greaves at Sefapane in the south" (`Objectives/A2SM05_APR_01`).
- The player crosses between them as a body in a truck: "They're hauling the dead out to some mass
  grave in the desert" (`Objectives/A2SM04_APR_01`). Before that, the game warns that "Once you move
  forward, you won't be able to return." (`Tutorial/TU76_MESSAGE`).

**Implied, from the map files**

- **The two maps are adjacent.** Each world's paper map is 5×5 pages of 1024×1024
  (`levels\w1_<row>_<col>\mapcompass.xbt` and `w2_…`). World 1's bottom row (`w1_e_1`–`w1_e_5`) is
  the same image as world 2's top row (`w2_a_1`–`w2_a_5`). The mean pixel difference is under 0.5/255,
  which is compression noise. So the two maps are two sheets of one map that overlap by one row of
  pages, with Bowa-Seko directly south of Leboa-Sako.
- **The diamond counts agree.** Each page prints its count of diamond cases:
  - World 1: 116 (pages `b2` 15, `b3` 12, `b4` 9, `c2` 15, `c3` 13, `c4` 12, `d2` 12, `d3` 12, `d4`
    12, `e2` 4).
  - World 2: 109 (`a2` 4, `b2` 8, `b3` 15, `b4` 15, `c2` 15, `c3` 12, `c4` 14, `d2` 15, `d3` 8,
    `d4` 3).
  - Counting the shared page's 4 cases once gives 221. That is the game's own total: "You found
    NUM_REPLACE out of 221 briefcases with diamonds" (`Diamonds/DiamondPickedSwoosh`).
- **They are linked by road and rail.** The railway leaves world 1 at its south-west (`w1_e_2`) and
  enters world 2 at its north-west, running down to Sefapane (`w2_a_2`, `w2_b_2`).
- **Between them lies an empty strip.** The shared row is desert and mountain with no labelled place
  on it, the land the player crosses in the mass-grave truck and the sandstorm.

## Leboa-Sako

| Map label | Page | What the game says |
| --- | --- | --- |
| Pala | `w1_c_3` | The town. "Pala only has one hotel still working." (`5160146`, taxi driver). Father Maliya's church is here (`4577402`, Reuben), and both factions' offices (`4967789`, Quiepo; `4972806`, Kankaras). Gakumba's office is "the abandoned bus station" and Kouassi's "the former bordello" (`Objectives/A1LM00_03`, `A1LM00_02`). |
| Mike's Bar | `w1_c_3` | "an expat bar -- good source for all sorts of shit" (`4967789`, Quiepo); "Lots of independent contractors there." (`4972806`, Kankaras). The buddies' base in Act 1. |
| Slaughter House | `w1_c_3` | Kankaras's camp, north of Pala (`5123416`, Kankaras). |
| Fresh Fish | `w1_c_3` | Carbonell's "fishing pier" or "fishing camp", north of Pala (`5123400`, Carbonell; `Objectives/A1BU03_01`). |
| Cock-Fights | `w1_c_3` | Quiepo's arena: "Big place used for chicken-fights" (`5121717`, Kankaras). |
| Lumber | `w1_c_3` | Purefoy's lumber camp (`5122321`, Carbonell). Later "that deserted lumber camp southeast of the town", where Reuben hides (`Objectives/A1SM03_01`). |
| Shwasana | `w1_b_3` | "the big fishing village at Shwasana", where "mercs from both sides are refueling their boats" (`Objectives/BSQ04_01`). The police chief's brother lives here (`4933190`, Paul Ferenc). |
| PetroSahel | `w1_b_2` | The fuel terminal the UFLL occupies. Every subtitle spells it "PetroSehel" (`4560449`, Marty Alencar). |
| North Railyard | `w1_b_2` | The railyard with the natural-gas tank cars (`Objectives/A1LM02_01`). |
| Cattle Xing | `w1_b_2` | — |
| Goka Falls | `w1_b_4` | "the Goka Falls retreat - north side of Mt. Thabamolaetsa" (`Objectives/A1SM02_APR_01`), with a lodge (`4973244`, Gakumba). |
| Fort | `w1_b_4` | "the legion fort", where the returned King stays (`4885625`, Flora Guillen). Earlier the base of "Commander Davenport" (`Objectives/BSQ05_01`). |
| Oasis | `w1_b_5` | "an oasis in the northeastern desert", where the King's gold sits "under a tarp" (`4558461`, Carbonell). |
| "Private Property" | `w1_c_4` | — |
| OGC (Occidental Growers Company) | `w1_c_4` | The greenhouses "in the foothills to the east", guarded by the APR (`Objectives/A1LM05_01`). |
| Police Station | `w1_c_2` | The police chief's base (`Objectives/A1LM01_04`). The prototype ammunition is in its garage (`Objectives/BSQ08_01`). |
| Scrap Salvage | `w1_c_2` | — |
| Claes Products (Chemical Corporation) | `w1_d_3` | — |
| Airfield | `w1_d_3` | "the airstrip south of the dump", where the crop-duster flies from (`Objectives/A1LM05_03`). |
| Mokuba | `w1_d_4` | "Mokuba shantytown" (`4889335`, Father Maliya). Here the foreign commandos are lured into a trap (`Objectives/A1LM04_03`). |
| Oasis | `w1_d_4` | "an oasis to the southeast", where the foreign commandos hide (`Objectives/A1LM04_01`). |

**Implied**

- **Unlabelled names.**
  - Mt. Thabamolaetsa is the big mountain north-east of Pala on `w1_b_4`. Dialogue puts it "one mile
    northeast of Pala" (`4889345`, Father Maliya) and Goka Falls on its north side.
  - The map prints no river names in the north.
- **Labels with no dialogue match it to a story site by its internal texture name**
  (`levels\w1_*\majorlocation_<name>.xbt`):
  - Cattle Xing is `ranch`, the cattle ranch where a UFLL official holds back a troop train
    (`Objectives/A1LM02_03`).
  - "Private Property" is `villa`, the villa where the Belgian informant lives (`4933594`, Michele
    Dachss) and behind which Commander Alessi's cargo sits on the docks (`Objectives/BSQ06_01`).
  - Scrap Salvage is `junkyard`, the UFLL motor pool "at the junkyard" (`Objectives/A1LM03_01`).
  - Claes Products is `chem_depot`, "an old chemical dump at the rail spur" (`Objectives/A1LM05_03`)
    where the APR keeps its "Agent Yellow" formula (`Objectives/BSQ10_01`).
- **The four lieutenants' camps ring Pala.** UFLL is to the north (Slaughter House, Fresh Fish) and
  APR to the south (Cock-Fights, Lumber), as the lieutenants say: "APR position south of you"
  (`5121717`, Kankaras).

**The game states (Underground cells)**

Four shops around Pala hide refugee families for the Underground: a veterinarian's clinic north-west of
Pala, a general store a mile south-west of it, an undertaker half a mile south of Mokuba, and a
well-and-windmill shop near the base of Mt. Thabamolaesta (`Objectives/A1GM01_01`–`A1GM04_01`).

## Bowa-Seko

| Map label | Page | What the game says |
| --- | --- | --- |
| Mosate Selao | `w2_c_3` | Called "Port Selao" in the journal and nearly all dialogue. "Mosate-Selao" is used only by Reuben (`4911115`) and one briefing (`4560094`, Tambossa). A cease-fire zone (`Tutorial/TU42_MESSAGE`), with both warlords' headquarters (`Objectives/A2LM00_01`) and Doctor Obua's clinic (`Objectives/A2SM06_APR_04`). |
| Sefapane | `w2_b_2` | "Town's split down the middle between the factions" (`Objectives/A2SM06_APR_01`). The APR and the UFLL each keep a command post here (`Objectives/BSQ20_01`, `BSQ21_01`), and there is a garage (`Objectives/A2LM09_03`). |
| Dogon Village | `w2_b_3` | The cliff village where the APR dries artemisia in kilns (`Objectives/A2LM10_01`): "Like a maze, all up and down the hill" (`4934434`, Xianyong Bai). |
| Sediko | `w2_b_3` | "the Dogon Sediko", Tambossa's base (`4561187`, Mbantuwe), where the APR holds a foreigner (`Objectives/A2BU07_01`). |
| Outpost | `w2_b_3` | The APR outpost where the medicine courier stops (`Objectives/A2LM10_03`). |
| Crash Site | `w2_b_4` | The salvage site of a wrecked Hind with a 500 lb bomb (`Objectives/A2LM11_03`). |
| Sepoko | `w2_b_5` | "an old border crossing" (`4561837`, Purefoy); "that sad excuse for a border crossing" (`4936185`, Marty Alencar). It has an Afreqa Telecom office (`Objectives/A2LM12_03_UAT_01`). |
| Polytechnic | `w2_c_4` | Where Seth Uniya teaches the APR bomb-making (`Objectives/A2LM12_01`). |
| Airfield | `w2_c_4` | The airport. Relief drops land here (`Objectives/A2LM12_05`), journalists are detained here at the end (`Objectives/A3SM14_01`), and the buddies' escape plane was to leave from here (`4898039`, Nasreen Davar). |
| Rail Xing | `w2_c_5` | — |
| Dental Clinic | `w2_c_2` | "an old dental clinic, west of Port Selao", with the nitrous oxide (`4560086`, Greaves). |
| Ranger Station | `w2_c_2` | — |
| Post Office | `w2_c_2` | "One kilometer to the west of Port Selao" (`5124224`, handler?). |
| Sehlakalase | `w2_c_3` | "Sehlakalase Island. Middle of Lake Segolo." (`5123973`, predecessor?): the Jackal's island, with his weapons cache and his house (`4559182`, Greaves; `5106733`, Reuben). |
| TaeMoCo Mine | `w2_d_2` | "the TaeMoCo diamond mine" (`Objectives/A2LM08_01`), which the buddies call a sham (`4933814`, Nasreen Davar). |
| Weelegol Village | `w2_d_2` | "the dried-up fishing village of Weelegol", where Liberation Radio's transmitter is parked (`Objectives/A2LM07_01`). |
| Mertens-Segolo Pipeline Company, M-S Pipeline | `w2_d_3` | The pipeline that pumps "fresh water out of Lake Segolo" to sell abroad (`Objectives/A2LM08_01`). |
| Bowaseko Brewing Co. | `w2_d_3` | "the Seko Brewery" (`Objectives/A2BU06_01`), Mbantuwe's base (`4560255`, Tambossa). |
| Prison | `w2_d_3` | Where the player and his buddies are held, and later the Jackal's way-station for the exodus (`LoadScreens/MiSyn_A3SM15`). |
| Marina | `w2_d_4` | "the Marina bar" (`Objectives/A3SM13_APR_01`), the buddies' base in Act 2 (`Objectives/BSQ14_02`). |
| Swamp, Heart of Darkness | `w2_d_4` | — |
| Landing Zone | `w2_d_4` | Where the arms merchant waits for "a chopper" with a briefcase of diamonds (`4920105`, Jackal). |

**The game states (unlabelled places)**

- **Lake Segolo**, the large lake at the map's centre (`Objectives/A2SM06_APR_02`).
- **The Noga River.** Yabek sits "on the East Branch of the Noga River now. Under the rail crossing."
  (`4560872`, Mbantuwe).
- **The Crossroads**, where the Voorhees column regroups (`4558910`, Voorhees).
- **A tourist resort**, home of the UFLL Propaganda Minister (`Objectives/A2LM07_03`).
- **The canyon pass and the Border Patrol camp**, at the end of the road out (`Objectives/A3SM15_04`,
  `A3SM15_05`). The Jackal waits for the diamonds "at a small hut just past the canyon truck pass"
  (`Objectives/A3SM15_02`).
- **The old prison** lies south of the airport: "You need to go to the Prison South of here"
  (`a3sm14_heartofdarkeness.a3sm14_mission:f_13_Out`, a designer note shown on screen).
- **Underground cells**: a general store at the Dogon River delta north of Port Selao, a phone-card
  shop a kilometre west of it, a hardware store north-west of the TaeMoCo mine, and a tobacconist on
  the east shore of Lake Segolo (`Objectives/A2GM01_01`–`A2GM04_01`).

**Implied**

- **Port Selao is the capital.**
  - Its map page texture is `majorlocation_capital` (`levels\w2_c_3`).
  - It is "the city" (`4559182`, Greaves; `4560386`, Voorhees) and the seat of both warlords.
  - It has the country's working airport, and the warlords say "the Port" (`4561603`, Voorhees).
  - "Mosate Selao" on the map and "Port Selao" in speech are one town: both are the place west of
    which the dental clinic stands (`4560094`, `4560086`).
- **Labels with no dialogue match it to a story site by texture name.**
  - Rail Xing is the rail crossing over the Noga's East Branch where Yabek's barge hides.
  - Heart of Darkness is `mouthofdarkness`. The same phrase names the airport mission's script,
    `a3sm14_heartofdarkeness`.
  - Page `w2_d_4` also holds a texture named `warlords`, implied to be the united leaders' bivouac.
- **The Marina is Bowa-Seko's Mike's.** Its texture is `majorlocation_mikesplace2`, and the Act 2
  buddies debrief there as the Act 1 buddies do at Mike's (`Objectives/BSQ14_02`, `BSQ02_02`).
- **"Seko" is the local beer.** Paul's "cases of Seko" (`5108747`, Paul Ferenc) and the Bowaseko
  Brewing Co. on the map point to it.
- **"The Crossroads" is Sefapane.** Voorhees's column says it will be "at the Crossroads"
  (`4558910`, Voorhees), and the player next meets him at Sefapane (`Objectives/A2SM06_UFLL_01`). The
  scripts name the Sefapane command posts and garage "Crossroads" (`A2SM00/BLUE_HQdoorman_Crossroads`,
  `A05Crossroads_EnemiesSTP`).
- **"Dust Town" is Weelegol.** The radio job's designer note says "Go to the Dust Town" for the
  transmitter at Weelegol (`a2lm07_radioarmageddon.a2lm07_mission:f_4_Out`; `Objectives/A2LM07_01`).

## What the war changes on the map

**The game states**

The mission scripts switch some places to a new state for the rest of the game:

| Where | What changes | When |
| --- | --- | --- |
| all of Leboa-Sako | the whole map turns to the colour of the faction that won Act 1 | `A2SM05` starts (`a2sm05_reprisal1.a2sm05_mission_forblue:In`) |
| the troop train's line, Leboa-Sako | the derailed train stays where it fell | `A1LM02`, on the buddy's plan (`a1lm02_reapsew.a1lm02_mission:f_34_Out`) |
| near the airstrip, Leboa-Sako | the wreck of the buddy's crop-duster stays | `A1LM05`, on the buddy's plan |
| the old marina's inlet | the arms barge lies sunk | `A2SM07` (`A2SM07_WreckedBarge`) |
| the TaeMoCo mine and dry riverbed | flooded | `A2LM08`, on the buddy's plan |
| Sefapane | the UFLL garage is destroyed | `A2LM09`, on the buddy's plan |
| the Dogon ruins | the APR's buried artemisia store is blown up and burns | `A2LM10`, on the buddy's plan |
| the Noga rail crossing | the rail span lies in the river | `A2LM11`, on the buddy's plan |
| the airport | the air drop's crates lie on the airfield | after `A2LM12`, either way |

Details and script citations are on [Act 1](./missions/act-1.md) and [Act 2](./missions/act-2.md). The
fallen rail span, the flooded mine and the burned store are not in a new game; they appear only once
those plans are carried out.

## Getting around

**The game states**

Each map has five bus stations: the town and stops in the four corner squares B2,
B4, D2 and D4. A ride takes four hours (`fasttravel.fasttravel:f_96_Use` onward).

## Spelling variants

| Map or journal | Elsewhere |
| --- | --- |
| PetroSahel (map) | PetroSehel (all subtitles) |
| Mosate Selao (map), Mosate-Selao (2 subtitles) | Port Selao (journal and other subtitles) |
| Mt. Thabamolaetsa (`Objectives/A1SM02_APR_01`) | Thabamolaesta (`4889345`, `Objectives/A1GM02_01`) |
| Leboa-Sako | Leboa-Seko (`4558791`) |
| Weelegol | Weeloegol (`Objectives/UbidaysLongDemo_03`, a demo string) |
| Seth Uniya (subtitles, journal) | Unyia (character tag `Seth_Unyia`) |
| King Nnyere (character tag, Flora) | Nnyele (most buddies) |
