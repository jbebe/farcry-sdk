---
sidebar_position: 2
---

# Catalogue of props and themes

The recurring vocabulary of the world: the props, decals and texts the designers reach for again and
again, what each one signals, and where to see it. Counts cover both campaign worlds, `w1` for
Leboa-Sako and `w2` for Bowa-Seko. How they were taken is on
[Reading the world](./reading-the-world.md).

:::info[Read from the retail world data]
Placed counts from every campaign `worldsector` and `landmarknear` file. Texts on signs and posters
were read off the textures themselves, rendered from `graphics\objects\_signskit\`. Nothing here was
checked in a running game.
:::

## Faction propaganda

Two poster sets and two graffiti stencils, one of each per faction, in
`graphics\objects\_signskit\factions\posters\`. All eight poster meshes of a faction draw from one
atlas texture, `posters_apr_d.xbt` or `posters_ufll_d.xbt`.

| | APR | UFLL |
| --- | --- | --- |
| Colours | red star on grey-green and black | black on yellow |
| Emblem | red star over a circle crossed by two bars | the outline of Africa holding a raised fist |
| Slogans | "Alliance for Popular Resistance", "APR Unite!", "People's Power", and "UFLL = Communist Dictatorship" | "United Front for Liberation and Labour", "UFLL Free Workers!", "Work to Fight / Fight to Work", "BeautiUFLL" |
| Imagery | rifles raised in a bunch, a soldier with a rifle, a snarling animal under the star | a fist, a man's face in glasses, soldiers marching across the map of Africa |
| Poster meshes placed | 228 (`posterapr_*`) | 245 (`posterufll_*`) |
| Graffiti placed | 24 (`graffiti_apr_01`), 22 of them in `w1` | 20 (`graffiti_ufll_01`) |

Two details are in the art itself. The APR set includes an attack poster, its own logo over the line
"UFLL = Communist Dictatorship". And every UFLL poster on the atlas is streaked with red spray paint;
*implied*, defaced, though the paint is baked into the texture and not a separate decal, so every UFLL
poster in the game carries it.

Who papers a place is the clearest ownership signal in the data, since the soldiers' archetypes are
not (see [Red is the UFLL](./reading-the-world.md#red-is-the-ufll-blue-is-the-apr)). Within 200 m of
each named place:

| Mostly APR | Mostly UFLL | Both |
| --- | --- | --- |
| Fort (28), "Private Property" (19), Mokuba (15), Lumber (10), Cock-Fights (9), OCG greenhouses (9), Rail Xing (7), Sediko (6), Airfield `w1` (4), Outpost (4), Polytechnic (4), Claes Products (3), Sepoko (3) | Shwasana (22), Petrosahel (14), Weelegol Village (14), North Railyard (11), M-S Pipeline (11), Cattle Xing (10), Slaughter House (9), Police Station (8), Fresh Fish (8), Taemoco Mine (8), Scrap Salvage (7), Bowaseko Brewing Co. (4) | Mosate Selao (56 APR, 54 UFLL), Pala (33, 44), Goka Falls (7, 5), Sefapane (20, 2) |

On the map this draws two clean territories. In Leboa-Sako the UFLL papers the north-west and the APR
the east and south; in Bowa-Seko the UFLL papers the south and west and the APR the north and east.
The two cities are papered almost equally, each HQ in its own faction's posters.

`logopanel01`/`logopanel02` in `graphics\objects\dfzkit\` are painted wooden boards carrying the
faction emblems (`factionslogo01_d.xbt`, APR on red, UFLL on yellow). They stand only at the
ceasefire checkpoints below.

## Ceasefire and the checkpoints that hold it

`sign_ceasefire` (`_signskit\ceasefire\`) is a spray-stencilled AK-47 under a red prohibition circle.
It is placed 68 times, and only in four places:

| Place | Stencils |
| --- | --- |
| Mosate Selao | 33 |
| Pala | 14 |
| the unlabelled `warlords` camp | 8 |
| Sefapane | 7 |
| elsewhere in `w2` | 6 |

In the two cities the stencils stand in tight clusters with `dzpole01`/`dzpole02` poles, `dzfences*`
fences and the faction logo boards, 90 to 250 m from the centre at separate points around the edge:
Pala has about nine such clusters, Mosate Selao about ten. *Implied:* checkpoints on the roads into town, each
marking where the guns are supposed to stop. They ring each town rather than running between its two
HQs. The `dz` prefix suggests "demilitarised zone"; the folder is `dfzkit`, and neither name is
expanded anywhere.

The warlords camp is the third kind of place they mark: a bivouac where both factions' commanders meet
(see [Bowa-Seko](./bowa-seko.md#the-warlords-camp)).

## Signs

`graphics\objects\_signskit\` holds the world's readable text. Most signs are one shared board mesh
with the words in a mask texture, red letters on magenta in the `_m.xbt`.

**Place names.** Dogon Village, Mokuba, Mosate Selao, North Railyard, Pala, Sediko, Sefapane,
Sehlakalase, Sepoko, Shwasana, Weelegol Village. They are placed as signposts at junctions, not at the
places: `MissionObjectiveSigns.PalaSign` stands 31 times out in the Leboa-Sako countryside, pointing
towards town, and `MosateSelaoSign` 29 times in Bowa-Seko.

**Services and trades.** Calling card and phone recharging stand, Cock-Fights, Dental Clinic, Fresh
Fish, General store, Hardware store, Lumber, Marina, Mike's Bar, Police Station, Polytechnic, Post
Office, Ranger Station, Safe House, Scrap Salvage, Slaughterhouse, Tobacco and News stand,
Undertaker, Veterinarian, Well and Windmill shop. Several are shops that exist only as a name on a
signpost and a pickup in a mission.

**Landmarks and road hazards.** Airfield, Fort, Goka Falls, Cattle Xing, Rail Xing. Road signs are a
colonial kit (`colonialstreetsignskit01_d.xbt`: STOP, one-way arrow, "Crossing Rail Road", chevrons,
no entry, no parking, speed limits 50 and 100) and yellow junction diamonds for crossroads, T and fork
(`intersectionsigns_d.xbt`; archetypes `CrossRoadSign_BK`, `TeaRoad_BK`, `ForkRoad_BK`).

**Icons without words.** The safe house sign is a pale tent shape on blue (`safehousesign_d.xbt`). The
weapons shop is a stencilled band of pistols and dollar signs (`weaponsshopsign_d.xbt`); the weapons
depot a crate (`weaponsdepotsign_d.xbt`). `MissionObjectiveSigns.WeaponShop` is placed 21 times in
Bowa-Seko alone.

**Bus stops.** `busmapw1`/`busmapw02` (16 each), `youarehere` and `destination01`: a route map, a
you-are-here board and a destination board, the fast-travel stops.

**Newspapers.** `newspapers512_d.xbt` is four front pages under a "NEWSPAPER POST" masthead; the body
text is illegible filler.

## Companies

Six corporate billboards in `_signskit\masters\`, the same brands on the shop-sign boards and as
`mesh:pub*` meshes:

| Brand | Billboard | Business, *implied* |
| --- | --- | --- |
| AfriQa Telecom | white logo with Africa in the Q, on green | phones |
| Claes Products (Chemical Corporation) | frogs on blue-green water | chemicals; its depot stores `DetailObjects.OldDefoliant` |
| Mertens-Segolo Co. | "Brings Water to your home!", workers in white coats before a pipeline | piped water from Lake Segolo |
| OGC, Occidental growers Company | farmworker silhouettes in a green field | export agriculture; owns the greenhouses |
| PetroSahel | a "PS" medallion between two fuel nozzles | fuel; owns the Leboa-Sako depot |
| Bowaseko breweries | "SEKO Pilsner — When nothing less than the best will do!" | beer |

Add Taemoco ("TAEMOCO MINE", `taemoco.xbt`) for diamonds and the world's economy is complete: oil,
water, chemicals, cash crops, diamonds, phones and beer. Only 46 billboard meshes are placed in both
worlds, and 17 of them are blank square and big boards (`pubboardsquare01`, `pubboardbig01`) in the
Scrap Salvage dump. *Implied:* advertising is something that used to happen here.

## Violence and its aftermath

| Prop | Placed | Notes |
| --- | --- | --- |
| `Corpses.Corpse` | 105 (`w1` 37, `w2` 68) | plus three `Corpses.AnimatedCorpse` in a truck and named mission bodies such as `PartnerMissions.GeorgianCorpse` |
| blood decals, `graphics\objects\bloodsplashes\` | 299 (`w1` 13, `w2` 286) | `bloodbigtrail_01` 132, `bloodpuddle_01` 100, `bloodsmalltrail_01` 34, `bloodhandtrail_01` 33 |
| flies, `environment.animals.flies` | 163 emitters | plus roaches and sound points such as `SoundPoint_RottingMeat_Flies` |
| burning, `Burn.BurnEmitter*` and `fires.background_fires.car_hull_fire` | 84 | smouldering wrecks and buildings |
| burned cars, `carburned01` / `Wreck.CarBurned01_BK` | 106 / 97 | |
| burned trucks and buses, `truckburned01` / `busburned` | 60 / 37 | |
| wrecked, unburned vehicles, `carwrecked*`, `truckwrecked*`, `Wreck.CarWrecked01_BK` | 155 | |
| crashed light aircraft, `crashed_airplane_*` | 9 | and `helicopterparts` at three sites |

Blood is almost absent from Leboa-Sako and everywhere in Bowa-Seko, and most of it belongs to a story
state rather than to the map: 150 decals and corpses are the layer `a2sm08_dogonring_trashed` at
Sediko, 22 `a2sm08_brewery_trashed`, 21 the prison mission `a2sm09`. *Implied:* the second half of
the game is the bloodier one by design, and its worst scenes are switched on by the story.

The blood decals read as actions. `bloodbigtrail` is a body dragged, `bloodhandtrail` a hand smeared
along the ground. At Sediko there are 26 hand trails among 47 drag trails and 37 puddles; at the prison
39 drag trails lead between the cells (`Prison.Prison_Blood_Trail`).

Of the 105 corpses, 37 lie within 2 m of a blood decal. The rest lie clean: shot, or left long
enough for the blood to go.

## Burial

134 grave markers: `tombstone01`–`03` (51) and `woodcross01`–`03` (83), from
`graphics\objects\_cemeterykit\`. All but three lone crosses form five grounds:

| Where | Markers | What surrounds them |
| --- | --- | --- |
| Mosate Selao churchyard | 22 stones, 6 crosses | the colonial church, benches, street lamps |
| hilltop south of Mokuba, (3,297, 1,252) | 25 stones, 18 crosses | six shovels, two hoes, two unburied corpses, the undertaker's shop |
| a field west of the Ranger Station, (1,227, 2,875) | 41 crosses, no stones | nothing within 45 m but one awning and a diamond briefcase |
| a hut camp south-west of the Marina, (3,142, 1,667) | 15 crosses in two straight rows | flies, gazelle bones, barrel fires, a hut |
| Sefapane | 4 stones | |

Stones mean an established cemetery; crosses alone mean recent dead buried fast. *Implied:* the town
cemeteries are old, and the war's dead are in the cross fields. The forty crosses alone in a field,
with no settlement, no church and no road furniture, are the closest thing in the data to a mass
grave.

## Animal bones

The most common sign of death in the world is not human. `bonesskullwildebeest` is placed 527 times,
and wildebeest and gazelle skulls, ribcages and scattered bones come to 1,686 pieces, plus 133
`desert_terrain.rocks_n_bones` clusters. They lie in the open savannah, at oases and around
villages. *Implied:* a dry country in drought, where herds die in the field.

## Fortification

| Prop | Placed |
| --- | --- |
| sandbag walls, `sandbagscover01`–`10`, `Military.SandbagsCover02_BK` | 1,388 |
| mounted guns in sandbags, `MountedWeapons.M249_SandBags`, `M2_SandBags`, `Mk19_SandBags` | 70 |
| road checkpoints, `CoverObjects.Checkpoint03_BK`, `CheckpointBarrier*`, `checkpointbarrier02` | 88 |
| outposts, `Infrastructures.StaticOutPost01` | 20 |
| watchtowers, `WatchTowerLeipzig`, `WatchTower01` | 9 |

A guard post is the same kit every time: a road junction with its signposts, sandbag walls, an
`M249_tripod`, a parked `Land.Rover`, a generator with lanterns (`Lighting.LanternExplotator`), an
ammo or explosives pile, and usually a wreck nearby. The checkpoints concentrate on the roads around
Mokuba and the Shwasana hills in Leboa-Sako.

## Trade and daily life

**Markets.** `Kiosk_W1_*` and `Kiosk_W2_*` stalls, straw awnings and `tablemarket01_multi`. Mosate
Selao has 92 pieces, Weelegol Village 48, Pala 30, Sefapane 20, Mokuba 10, and they barely appear
anywhere else. Trade survives in the towns.

**Food.** Rice bags, mango and banana stacks, cooking pots and fires, `cauldronmetalhut`. Meat props
(`Meat04`, `cutmeat04`, `MeatCarcassHook`, `SlaughterBuggy`) belong to the Slaughter House and a few
camps, with flies over them.

**Beer.** `DetailObjects.BeerBox01`, `BeerBarrel`, beer crates as seats. The AI points
`Rest.BeerCrate`, `Rest.DrinkBeerCrateA` and `Social.DrinkBeerCrateB` are placed 157 times each. Beer
is the soldiers' leisure, and the brewery is a front-line asset.

**Litter.** `trashborder01`, `trash01`–`05`, `trashground*`, garbage bags and newspapers are in every
inhabited place; Pala alone has 182 `trashborder01`. Laundry lines (`laundry01`) mark where people
sleep.

**Comfort.** `ComfyBed`, `sofabuddy` and boom boxes cluster in a few places only: Mokuba's shacks,
the "Private Property" villa with 13 boom boxes and 8 portable TVs.

## Diamonds

224 `MissionPickups.DiamondBriefcase_*` (also spelled `DiamondBiefcase`), graded `LVL1`–`LVL3`. Most
lie in the wild, on rooftops, in ruins and in wrecks: 35 within 5 m of a wreck. 25 lie within 5 m of a
corpse. *Implied:* a smaller motif of the courier who died carrying them, which the corpse-and-blood
sites in the Bowa-Seko south make vivid (see [Bowa-Seko](./bowa-seko.md#beyond-the-named-places)).

## What the soldiers do

AI "smart objects" are placed points where a soldier performs a routine; each is named for it. They
are set dressing too, the designers deciding what life looks like at each post. Across both worlds:

| Routine | Placed |
| --- | --- |
| `Social.RandomConversation` | 953 |
| `Rest.Smoke` | 861 |
| `Duty.Guard` | 699 |
| `Rest.SitOnGround` | 449 |
| `Rest.Piss` | 392 |
| `Duty.FixWeapon` | 174 |
| `Rest.BeerCrate` | 157 |
| `Layered.SlapInsects` | 141 |
| `Rest.TieShoes` | 129 |
| `Rest.CellPhone` | 119 |
| `Duty.WriteOnNotepad` | 118 |
| `Duty.CultivateField` | 99 |
| `Duty.Fishing` | 47 |
| `Social.Boxing` | 46 |
| `Social.Dice` | 32 |
| `Social.Awale` | 29 |
| `Duty.DiamondSearch` | 25 |
| `Duty.Loot` | 19 |
| `Duty.PeelPotatoes` | 16 |

Smoking, talking and urinating outnumber guard duty. The soldiers farm, fish, play dice and awale
(the African board game), box, and search for diamonds in the mine and the swamp. *Implied:* a war
fought mostly by bored men waiting, who live off the land they hold.

## Civilians

30 placed civilians in both worlds: `Civilians.Male_Civilian` 19, `Female_Civilian` 6,
`Female_Civilian_WithDress` 4, and one for a mission. Five of them are at the Bowa-Seko Airfield.
Against about a thousand soldiers, this is the strongest statement in the data. *Implied:* the
country has emptied; the people who are left are waiting to leave.
