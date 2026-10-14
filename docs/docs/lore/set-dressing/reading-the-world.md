---
sidebar_position: 1
---

# Reading the world

Far Cry 2 tells most of its story without words. A village with blood dragged across every doorstep,
a town ringed by spray-painted ceasefire stencils, a field of forty wooden crosses with nothing else
near it: the designers placed these, prop by prop, and the placements survive in the shipped world
files. These pages read them back.

This page covers the method, the names of the places, and two keys you need before reading the rest:
which faction is which in the data, and how mission state changes a place. The
[catalogue](./catalogue.md) lists the recurring props and what they signal. The two region pages,
[Leboa-Sako](./leboa-sako.md) and [Bowa-Seko](./bowa-seko.md), go place by place.
[Hotspots and landmarks](./hotspots.md) finds the dressed scenes the map does not name.

:::info[Read from the retail world data]
Every `worldsector*.data.fcb` and `landmarknear*.data.fcb` of both campaign worlds, `world1` and
`world2`: 9,056 files and 184,536 placed entities, decoded with `jackall-cli fcb decode`. Place names
come from the map tiles and sign textures in the same archives. Nothing on these pages was checked in
a running game.
:::

## How the survey was done

Every placed entity has a name, a global position and, usually, a mesh. Entities placed from an
archetype carry the archetype's path as their name with an instance number on the end
(`Wreck.CarBurned01_BK_7`, `Rest.Piss_12`); hand-placed meshes are `StaticObject_<n>` with a
`text_objModel` path. Either way the name says what the thing is. How placements, archetypes and
mission layers work is in [Entities](../../engine-internals/entity-instancing.md).

Each named place was surveyed by counting everything within 150 m of its map marker, leaving out
terrain rocks, vegetation and pure logic nodes. Counts on these pages are within that radius unless
they say otherwise. Props that cluster away from any named place were found by grouping each prop type
by its nearest marker and looking at the ones that fell outside every radius.

Positions are global metres: `x` runs east, `y` runs north. The playable map covers roughly 1,024 to
4,096 on both axes, so a world's centre is near (2,560, 2,560).

## How claims are cited

These pages follow the convention of the [sources page](../sources.md), with placements in place of
strings:

- **Placed** — the world data contains it. The claim names the asset or archetype and, where it
  helps, the position: `Corpses.Corpse` x15 at Sediko, `mesh:woodcross01` at (1,227, 2,875).
- **Implied** — what the placements suggest. Each implied claim says what it rests on, and is worded
  as a reading, not a fact.

A few claims quote the string table, as `Objectives/<key>` or a subtitle's sound id, where a place's
props only make sense with the mission that uses them.

## The gazetteer

Each named place on the in-game map is a `CMajorLocationEntity` named `MajorLocation_*`, and each one
points at a `levels\<level>\majorlocation_<name>.xbt` texture: the zoomed map tile, with the place's
name printed in its corner. The texture's file name is the designers' name for the place; the printed
label is the player's. There are 45. What the dialogue and the paper map call these places, and how
the two maps join, is on [Places](../places.md).

### Leboa-Sako (`world1`)

| Map label | Internal name | Level | Position |
| --- | --- | --- | --- |
| Petrosahel | `fuel_depot` | `w1_b_2` | 1,405, 3,914 |
| North Railyard | `railyard` | `w1_b_2` | 1,209, 3,325 |
| Cattle Xing | `ranch` | `w1_b_2` | 1,888, 3,487 |
| Shwasana | `fishing_village` | `w1_b_3` | 2,310, 3,335 |
| Goka Falls | `fool_errand` | `w1_b_4` | 3,181, 3,592 |
| Fort | `old_fort` | `w1_b_4` | 3,510, 3,764 |
| Oasis | `king_oasis` | `w1_b_4` | 3,961, 3,974 |
| Police Station | `gncamp` | `w1_c_2` | 1,281, 2,620 |
| Scrap Salvage | `junkyard` | `w1_c_2` | 1,676, 2,246 |
| Cock-Fights | `arena` | `w1_c_3` | 2,197, 2,151 |
| Mike's Bar | `mikesplace` | `w1_c_3` | 2,608, 2,150 |
| Lumber | `lumberyard` | `w1_c_3` | 2,910, 2,197 |
| Pala | `town` | `w1_c_3` | 2,590, 2,483 |
| Slaughter House | `slaughterhouse` | `w1_c_3` | 2,199, 2,782 |
| Fresh Fish | `fishingcamp` | `w1_c_3` | 2,785, 2,916 |
| OCG (Occidental growers Company) | `greenhouses` | `w1_c_4` | 3,656, 2,645 |
| "Private Property" | `villa` | `w1_c_4` | 3,325, 2,713 |
| Airfield | `w1d3_airstrip` | `w1_d_3` | 2,526, 1,189 |
| Claes Products (Chemical Corporation) | `w1d3_chem_depot` | `w1_d_3` | 2,302, 1,786 |
| Oasis | `w1d4_dsoasis` | `w1_d_4` | 3,937, 1,196 |
| Mokuba | `w1d4_shantytown` | `w1_d_4` | 3,293, 1,880 |

### Bowa-Seko (`world2`)

| Map label | Internal name | Level | Position |
| --- | --- | --- | --- |
| Sefapane | `crossroads` | `w2_b_2` | 1,771, 3,549 |
| Sediko | `dogonring` | `w2_b_3` | 2,912, 3,230 |
| Dogon Village | `dogonvillage` | `w2_b_3` | 2,231, 3,619 |
| Outpost | `outpost` | `w2_b_3` | 2,801, 3,760 |
| Crash Site | `crashsite` | `w2_b_4` | 3,291, 3,531 |
| Sepoko | `borderpost` | `w2_b_4` | 3,936, 3,580 |
| Post Office | `postoffice` | `w2_c_2` | 1,688, 2,367 |
| Ranger Station | `touristresort` | `w2_c_2` | 1,387, 2,529 |
| Dental Clinic | `hutvillage` | `w2_c_2` | 1,826, 2,877 |
| Mosate Selao | `capital` | `w2_c_3` | 2,592, 2,520 |
| Rail Xing | `armsbazaar` | `w2_c_4` | 3,950, 2,347 |
| Airfield | `airport` | `w2_c_4` | 3,252, 2,504 |
| Polytechnic | `polytechnique` | `w2_c_4` | 3,300, 2,980 |
| Taemoco Mine | `diamondmine` | `w2_d_2` | 1,788, 1,711 |
| Weelegol Village | `dusttown` | `w2_d_2` | 1,411, 1,341 |
| Bowaseko Brewing Co. | `brewery` | `w2_d_3` | 2,809, 1,768 |
| M-S Pipeline | `pipeline` | `w2_d_3` | 2,231, 1,514 |
| Mertens-Segolo Pipeline Company | `pumphouse` | `w2_d_3` | 2,570, 1,610 |
| Prison | `prison` | `w2_d_3` | 2,784, 1,153 |
| Heart of Darkness | `mouthofdarkness` | `w2_d_4` | 3,160, 1,202 |
| Swamp | `swamp` | `w2_d_4` | 3,454, 1,214 |
| Landing Zone | `lz` | `w2_d_4` | 3,754, 1,222 |
| *(no label)* | `warlords` | `w2_d_4` | 3,678, 1,472 |
| Marina | `mikesplace2` | `w2_d_4` | 3,345, 1,567 |

### Where the two names disagree

The internal names were working titles, and several say more than the label does:

- **`fool_errand` → Goka Falls.** Goka Falls is where both faction bosses send the player to kill the
  other, and where the player is then set up (`Objectives/A1SM02_APR_02`). The designers named the
  place after the trap.
- **`villa` → "Private Property".** The only label printed in quotation marks.
- **`hutvillage` → Dental Clinic.** A jungle hut village with no medical prop in it (see
  [Bowa-Seko](./bowa-seko.md)).
- **`touristresort` → Ranger Station**, and **`armsbazaar` → Rail Xing**: the label names the
  building, the internal name the business.
- **`borderpost` → Sepoko**, **`dogonring` → Sediko**, **`capital` → Mosate Selao**.
- **`mouthofdarkness` → Heart of Darkness.** The label settled on Conrad's title.
- **`warlords`** is the one tile with no printed label at all.
- **`arena` → Cock-Fights**, whose sign texture is `dogfights_m.xbt` and whose signpost archetype is
  `MissionObjectiveSigns.DogFightsSign`, though the sign reads "Cock-Fights".

The spellings drift between assets as well. The greenhouse company is "OGC" on its sign
(`ogc_m.xbt`), its billboard and the paper map page `w1_c_4`, but "OCG (Occidental growers Company)"
on its zoomed location tile, `majorlocation_greenhouses.xbt`. The capital is "Mosate Selao" on its sign and
tile, while every line of dialogue calls it Port Selao (`4897437`, "Welcome to Port Selao!"). The
brewery is `bowaseko_d.xbt` ("Bowaseko breweries"), `sakobreweries_m.xbt` ("SAKO BREWERIES") and
`sekobeer_billboard_d.xbt` ("SEKO Pilsner").

## Red is the UFLL, blue is the APR

The soldier archetypes are named by colour, not faction: `enemy_archetypes.Red_Faction.*` and
`enemy_archetypes.Blue_Faction.*`. The data settles which colour is which:

- The mission layer `missions\librarymissions\a1lm00\red_hqdoorman` holds `UFLL.UFLL_HQDoorman`, and
  `blue_hqdoorman` holds `APR.APR_HQDoorman`. Bowa-Seko repeats the pairing in
  `a2sm00\red_hqdoorman_crossroads` and `blue_hqdoorman_crossroads`.
- Within 45 m of the UFLL doorman in Pala stand 12 `Red_Faction` soldiers, 37 UFLL posters and the
  AI points `..._RedFaction`. Within 45 m of the APR doorman stand 12 `Blue_Faction` soldiers, 26 APR
  posters and the points `..._BlueFaction`. Mosate Selao's two HQs match: 15 red and 48 UFLL posters
  around one, 15 blue and 44 APR posters around the other.

This runs against the art: the APR's posters are built around a red star, and the UFLL's are yellow
and black (see the [catalogue](./catalogue.md#faction-propaganda)).

### A soldier's colour says nothing about who holds a place

Nearly every placed soldier is blue. Across both worlds, 991 placed soldiers use a plain
`Blue_Faction` archetype and 71 a `Red_Faction` one; 41 of the red ones stand in Pala, Sefapane and
Mosate Selao, the three towns with a UFLL HQ. A
guard post deep in UFLL country is manned by `Blue_Faction.Assault_Nubian` all the same. *Implied:* the placed archetype is a default
merc, and whatever decides a guard post's allegiance at runtime is not the archetype name. Who holds a
place is better read from its posters and graffiti.

## A place has more than one state

Every placed entity belongs to a mission layer, and only the `main` layer is always on. A place can
therefore exist in several versions, switched by the story; how this works is in
[Mission layers](../../engine-internals/entity-instancing.md#mission-layers-decide-when-a-placed-entity-exists).
The set dressing uses this heavily:

- Sediko's massacre is the layer `a2sm08_dogonring_trashed`: 192 entities, 150 of them blood decals
  and corpses. The village is clean until the story turns it.
- Pala's interiors exist twice: `w1c3_broken_interior` (129 entities) and `a1sm01_cleaninterior`
  (111).
- Taemoco Mine has `diamond_mine_not_flooded_gameplay` (240) and `diamond_mine_flooded_gameplay`
  (51).
- Rail Xing's bridge has a fallen state, `staticfallenbridgepieces` (132) and
  `misnsubv_wreckedbridgebargepieces` (144), off until `A2LM11` drops the span.
- Mosate Selao has a whole after-state, `leveldesign\w2c3\a10_capital\post_hornestnest` (646).

So a count on these pages is the sum of every state of a place. Where a prop belongs to one state,
the page says which.

## Open questions

- **What sets a guard post's allegiance at runtime?** The archetype colour does not (see above).
  Tracing the faction field read by `CFCXAIComponent` would answer it.
- **The desert mass grave of `Objectives/A2SM04_APR_01` is not placed as a grave.** The layer
  `a2sm04_se01_truck_and friends` holds a truck (`World1_DesertShelter.BigTruckCompound`) and three
  `Corpses.AnimatedCorpse` at (1,262, 842), and no pit, crosses or bodies on the ground. Whether the
  grave exists only in the scripted scene's cinematic (`moviedata.xml`) was not checked.
- **Do the `_trashed` layers stay on after their mission?** The mission definitions that list which
  layers each mission enables were not read.
- **What changes in Mosate Selao after `post_hornestnest`?** The 646 entities were counted, not
  compared against the before-state.
- **What is `gncamp`?** The internal name of the Police Station is unexplained.
- **What is the "DunkCage" at Fresh Fish?** The data has `DunkCageOffice` and
  `ReinforcementPoint_DunkCage` and nothing that says what is dunked.
- **Which plants grow where?** Most vegetation is placed through collections whose resource ids are
  not yet resolved to species ([Terrain and vegetation](../../engine-internals/terrain-and-vegetation.md)),
  so these pages make no claim about biomes.
- **Should the pages show the textures?** The posters, signs and map tiles are described in words.
  Embedding renders of the game's own art on a public site was left for the maintainer to decide.
- **Nothing here was seen in game.** A placed prop can be hidden by its layer, out of reach, or
  never streamed in.
