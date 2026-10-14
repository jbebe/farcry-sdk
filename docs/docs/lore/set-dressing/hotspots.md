---
sidebar_position: 5
---

# Hotspots and landmarks

The map names 45 places. The designers dressed many more. Wherever authored props crowd together
away from a named place, someone built a scene: a train ambushed with its dead still aboard, a
hilltop where a grave is being dug, a crashed glider with its pilot beside it. Some of these scenes
belong to a mission; many belong to nothing at all. Both kinds are listed here, because a dressed
place with no story attached is a place ready for one.

:::info[Read from the retail world data]
Every campaign `worldsector`, `landmarknear` and `landmarkfar` file: 191,825 placed entities. Function
markers (safe houses, shops, mission objectives) from each world's `mapsdata.fcb`. Nothing here was
checked in a running game.
:::

## How the hotspots were found

Raw density finds the wrong things: forests, rock fields, litter. The search was refined in five
steps:

1. **Keep only authored props.** Drop vegetation, terrain rocks, litter and debris, fences, roads and
   track, bones, and every invisible entity (AI points, triggers, lights, sounds, spline volumes).
   Keep particles only when they show fire, smoke or flies.
2. **Weight by rarity.** Each prop scores `log(total props ÷ copies of that prop)`, so a crashed plane
   outweighs a crate. A prop type counts at most four times per cell, so a varied scene beats a pile
   of identical boxes.
3. **Score the ground.** Sum the weights in 16 m cells, smooth over a 48 m window, take the local
   peaks, and merge peaks closer than 60 m. 390 hotspots score 60 or more.
4. **Explain what can be explained.** A hotspot within 60 m of a function marker in `mapsdata.fcb` gets
   that marker's role. One within 200 m of a named place is "in place". The rest are **unexplained**:
   dense, authored, and attached to nothing on the map.
5. **Find the lone landmarks separately.** A crashed helicopter is one object, so density misses it.
   Meshes from the aircraft, wreck, building and infrastructure folders that are placed four times or
   fewer per world were listed wherever they stand.

| Role | Hotspots | What it is |
| --- | --- | --- |
| safe house | 65 | a `SafeHouse*` marker; the bed, crates and save point of a safe house |
| guard post | 57 | an ammo, health, fuel or explosives pile (`Objectives.PGP.*`) |
| in place | 53 | inside one of the 45 named places |
| mission site | 53 | a mission objective marker |
| spawn point | 16 | a named spawn point (bus stops, buddy rescues, scripted scenes) |
| underground cell | 9 | an Underground location (`Objectives.GRIN*`) |
| weapon shop | 9 | a weapon shop |
| bus stop | 7 | a bus stop |
| HQ, cell tower | 3, 2 | |
| **unexplained** | **116** | none of the above |

By average score the densest places are the faction HQs (about 3,000), then the weapon shops (about
1,700), whose walls of weapon hooks and crates of ammunition outscore every village. Mission sites
and underground cells follow (about 1,000), then guard posts and safe houses (about 500). The
unexplained hotspots average 157: mostly small scenes, with a few as rich as a safe house.

## The two maps overlap

The Jackal's desert shelter is built twice, once in each world, with the same props in the same
places: `housemedium02`, `mace01`, `shovel02` and `barrel02` stand at (1,286, 671) in Leboa-Sako
(`World1_DesertShelter.*`) and at (1,286, 4,767) in Bowa-Seko (`JacksHideout.*`). The broken railway
with its caboose, locomotive and wagons repeats the same way, at (1,650, 860) and (1,648, 4,900). The
offset is exactly 4,096 m in `y`. *Implied:* Leboa-Sako's southern edge and Bowa-Seko's northern edge
are one strip of country, built in both worlds so the player crosses from one to the other through
the same scenery. Both copies lie outside the playable square of their world.

## Leboa-Sako: scenes without a name

**The ambushed troop train**, 400 m east of Petrosahel at (1,830, 4,000). Carriages and wagons
derailed on broken track (`infra_train_track_broken`), 38 panes of `PassengerTRainGlassBK`, 12
`Corpses.Corpse`, military crates and camouflage backpacks, rubble, dark smoke, and a
`MissionObjectives.TrainFuelCar_BK`. 290 entities sit in the layer `a1lm02\wager_trainwreck`: the
train "carrying the UFLL troops" that the buddy ambushes (`Objectives/A1LM02_05`).

**The opening road**, 250 m west of Pala at (2,350, 2,340). Burning car hulls, three
`introtrucktroops` lorries, a scripted convoy (`ConvoyBigTruck1`–`3`, `ConvoyEscort1`,
`ConvoyInterceptor`) and roadside shacks and kiosks, all in `openingsequence\taxiride`. Further south,
at (2,856, 1,416), the taxi passes a roadside village with ten civilians, two scripted cape buffalo and
a scripted Datsun (`A1SM00.Event_40_Civilians`): the only crowd of ordinary people in Leboa-Sako.

**The convoy ambush** of `a2sm05`, at (1,850, 1,720): burning wrecks along the road, looters
(`Duty.Loot` x7) and spectators (`Duty.Guard_Spectator` x7) standing over them.

**The fallen crop-duster**, near the villa at (3,515, 2,838): `airplane_ba4b_blueflyer01` with a
`SoundPoint_Defoliant_Plane`, in the layer `a1lm05\wager_crashsite`. The buddy loads OGC's crop-duster
with defoliant and is "shot down" (`Objectives/A1LM05_05`, `A1LM05_06`).

**Three planes with tapes.** A crashed blue BA4B alone at (1,498, 1,416) with nothing but a
`JackalTape` beside it. A crashed blue Cessna at (3,029, 1,225) with a Jackal tape, a buggy, a lean-to
and a sandbag ring. Another crashed Cessna at (2,386, 3,806), still smoking, with a backpack, a
syrette and a pen: a buddy's scene (`A1BU08.A1BU08`).

**The broken bridge**, at (1,656, 2,984): a metal bridge with a broken span (`metalbridge_midbrk`),
a corpse and a diamond briefcase beneath it, and a signpost to seven places on the road.

**An oil seep**, at (1,816, 2,568): `oilrock03` and `semioilrock03` beside burning vehicles and seven
`Faction_01.Grunt` in the layer `a1lm01\wager_enemies`.

**An unnamed fishing hamlet** south-east of the Fort, around (3,970, 3,240): fishing houses, a
store with shelves of cans, bottles and crates, pirogues, fishnets, barrel fires, and soldiers who fish
(`Duty.Fishing` x3). It has no map label and no mission layer.

**An unnamed shanty settlement** between Fresh Fish and Shwasana, at (2,808, 3,352): eleven shanty
walls, four beds, nine barrel fires, torches, breakable fences, a diamond briefcase. No label, no
layer.

**Shacks with the TV on.** `Appliances.TvBIG_On` stands in a shack at (3,416, 1,672) with sofas,
shelves, laundry and a bed, in another at (3,320, 1,368) with a mattress and a bedroll, and at a guard
post at (3,498, 1,627). That guard post also has a **soccer goal** (`soccergoal`), a goat, cooking pots,
and soldiers peeling potatoes and giving first aid.

**The Underground cells** each have a trade. At (1,640, 1,272), a supply stash: ten rice sacks, beer,
plastic bowls, TVs. At (3,800, 3,378), a farm with five windmill wheels and water pumps, hoes and
shovels. At (1,576, 3,192), cages and a wheelbarrow. At (3,293, 1,255), the undertaker's cemetery. Each
shelters three civilians.

## Bowa-Seko: scenes without a name

**The Jackal's island arms cache**, on Lake Segolo at (3,032, 2,168). 61 military crates, sandbag
positions, three `Mk19` grenade launchers, wall and desk maps, and the barge
(`MissionObjectives.HornetsNestBarge`) at its dock: 161 entities in `a2sm06_ferryboat`. *Implied:* the
"massive weapons cache on his island" (`4559182`) that the barge carries into the capital.

**The derailed train people live in**, at (1,064, 3,288). A locomotive and passenger carriages off
the rails, and inside them laundry lines, bedrolls, 17 loose sheets of paper, books and a camping
lamp, with its own lighting and scripted event (`Train.SE01Train_LA`). A mission objective marker
stands 3 m away, but the map gives the place no name.

**The derailed freight train**, at (1,079, 3,780): a locomotive and five metal wagons, a diamond
briefcase, gazelles grazing, and a single wooden cross beside the track.

**The burial on the hill** above Taemoco Mine, at (2,024, 2,008), 84 m up. Five shovels, two
corpses, a campfire and a Jeep. No layer. *Implied:* the dead are being buried as you arrive.

**The bus execution**, 300 m south of Weelegol, at (1,338, 1,040): ten `Corpses.Corpse`, 24 blood
decals, a burned bus, a wrecked car, a Datsun, and `Carver's tape 7`.

**The crashed glider**, at (1,800, 1,320): `veh_glider` with one corpse and a diamond briefcase
beside it. Eleven entities, no layer.

**The courier plane**, at (3,940, 2,829): a crashed blue Cessna, two courier pouches
(`courierpouch`), a walkie-talkie, a Carver tape, and cape buffalo grazing around it.

**A truck wreck with its dead**, near Sediko at (3,120, 3,330): a wrecked truck, two corpses, a boom
box, a Carver tape.

**The Hind.** `graphics\vehicles\wrecks\hindhelicopter\helicopterparts.xbg` lies in three places: at
the Crash Site with the three `500lbBomb` objectives and the bomb's fuze (`500lbBombFuse`), at the
Landing Zone, and on a fortified hilltop above the Prison at (2,837, 1,142, 121), among sandbags,
prison doors and a save point. The APR salvaged "a 500lb. bomb … from a wrecked Hind"
(`Objectives/A2LM11_03`).

**The C-130**, 80 m north of the Polytechnic at (3,289, 3,062, 60):
`VisualObject_A2LM12_C130Aircraft` with four exhaust emitters, in `a2lm12\misnsubv_planeflight`.

**The predecessor's camp**, at (2,968, 2,120): a hut, ten paper desks, four blue passports, flies and
roaches, a bedroll and a Carver tape. The layer `a3vm09\vmend` holds his corpse and dictaphone.

**The finale.** The Jackal's cabin at (3,928, 1,608): `JackalCabin.JackalCabin_Lighting`,
`Jackal.Jackal_SpecialCharacter`, eight `World3_CanyonPass.DiamondRough`, a briefcase of money and the
car battery for the dynamite (`World3_CanyonPass.BatteryCar`), in `a3sm15_finalbriefing`. The border
post at (3,950, 1,805), 106 m up: an industrial guard tower, security fences, three big trucks,
loudspeakers, border guards (`MissionSpecific.Border_Guard`), passports, cigarettes and radios, and
the ending's camera (`A3SM15_EndCineCam`).

**Markets on the roads.** A livestock market at (1,608, 1,528) with over 20 cages and kiosks; two
roadside markets south-west of the capital at (2,216, 2,760) and (2,248, 2,696), with sorghum
baskets, mangoes, fruit and a Mertens-Segolo billboard.

**The checkpoint bridges into the capital.** At (2,792, 2,536) a metal road bridge carries eight
ceasefire stencils and twelve DZ poles; at (2,472, 2,312) a wooden bridge carries six stencils and
faction logo boards.

**Camps.** A paraglider launch on a hill at (2,488, 3,160) (`Air.Paraglider.Paraglider_Lv5`, straw
shelters, statue benches); a riverside landing at (2,600, 2,920) with pirogues, a swamp boat, duffel
bags and a campfire; a burned bus with a camp of cauldrons and a boom box at (1,320, 3,224).

**The Underground cells** each have a trade. Cigarette smugglers at (3,416, 2,248): 14 cartons,
cigars, lighters, beer. Miners at (1,560, 1,928): seven pickaxes, nine shovels, welding torches,
power wrenches. An organisers' office at (2,280, 3,208): six desk phones, cork boards with papers,
school tables, TV antennas. Each shelters three civilians.

**A lone water tower** on a hilltop at (1,999, 2,061, 68), with nothing else within 25 m.

## Story-ready sites

The places below are dressed but carry no mission layer and no function marker: a scene with
nothing to play in it. They are the strongest candidates for new stories.

| Where | World | Position | What is there |
| --- | --- | --- | --- |
| the derailed freight train | Bowa-Seko | 1,079, 3,780 | locomotive, wagons, one wooden cross, a briefcase |
| the burial on the hill | Bowa-Seko | 2,024, 2,008 | five shovels, two corpses, campfire, Jeep |
| the crashed glider | Bowa-Seko | 1,800, 1,320 | glider, dead pilot, diamonds |
| the bus execution | Bowa-Seko | 1,338, 1,040 | ten corpses by a burned bus |
| the field of crosses | Bowa-Seko | 1,227, 2,875 | 41 wooden crosses, nothing else |
| the lone water tower | Bowa-Seko | 1,999, 2,061 | a tower on a hilltop |
| the livestock market | Bowa-Seko | 1,608, 1,528 | cages, kiosks, meat |
| the riverside landing | Bowa-Seko | 2,600, 2,920 | pirogues, swamp boat, duffel bags |
| the fishing hamlet | Leboa-Sako | 3,970, 3,240 | fishing houses, a stocked store |
| the shanty settlement | Leboa-Sako | 2,808, 3,352 | beds, barrel fires, torches |
| the broken bridge | Leboa-Sako | 1,656, 2,984 | a broken span, a corpse, a briefcase |
| the lone BA4B | Leboa-Sako | 1,498, 1,416 | a crashed plane and a Jackal tape |
| the TV shacks | Leboa-Sako | 3,416, 1,672 and 3,320, 1,368 | homes with the television on |

The [catalogue](./catalogue.md#burial) and the region pages cover the cemeteries and the corpse sites
in more detail.

## Room for new events

Which places are free for new missions and small events? A site counts as **mission-linked** if it
holds an entity in a mission layer (story, faction contract, buddy unlock or side quest,
assassination, convoy, gun-shop, Underground, Carver tape, opening sequence, random encounter, or a
`_disableformission` layer), or a mission marker in `mapsdata.fcb`. The radius is 200 m for a named
place and 60 m for a hotspot. Safe houses, the weapon bazaar, guard-post supply piles and the
benchmark do not count as missions.

| | Leboa-Sako | Bowa-Seko |
| --- | --- | --- |
| named places used by one or two missions | 5 of 21 | 8 of 24 |
| mission-free dressed scenes | 34 | 48 |
| mission-free safe houses, guard posts and bus stops | 32 | 31 |
| 64 m squares of the playable map with no authored prop | 1,603 of 2,304 (70%) | 1,511 of 2,304 (66%) |
| squares holding any mission entity | 202 (9%) | 187 (8%) |
| quiet road (three props or fewer, no mission in the 64 m squares around it) | about 9 km | about 12 km |

Every named place is used by at least one mission, but missions run once. After its mission, a place
stays dressed and garrisoned and nothing else happens there. The empty squares include lake, cliff
and steep slope; they were not checked against the AI navigation mesh.

### How to find a site

Every entry below gives the same four locators:

- **Position** in global metres, as in an entity's `hidPos`: `x` runs east, `y` north, `z` is height.
- **Level** and **sector**: the site's objects are in
  `levels\<level>\generated\worldsectors\worldsector<sector>.data.fcb`. The sector is
  `floor(y / 64) × 80 + floor(x / 64)`. In the level name the letter runs north to south (`a` covers
  `y` 4,096 to 5,119, `e` covers 0 to 1,023) and the number west to east (`1` covers `x` 0 to 1,023).
- **Map tab key**: an object placed at the site. In JackAll's [Map tab](/jackall/map-viewer),
  load the world, type the key into the hierarchy filter and click the match; the camera flies there.
  "One of N" means N objects share the name; pick the one whose inspector shows the listed sector.
- **In game**, nothing yet teleports to a coordinate. DevTools has `teleport_to_current_objective`
  and a free camera only.

### Start here

Dressed, story-ready, and attached to nothing. The two marked `carvertapes` hold only a collectible
tape.

| World | Site | Missions within 60 m | Level | Sector | Position (x, y, z) | Map tab key |
| --- | --- | --- | --- | --- | --- | --- |
| Bowa-Seko | the derailed freight train | none | `w2_b_2` | 4736 | 1,079, 3,780, 25 | `StaticObject_107559` (one of 4) |
| Bowa-Seko | the burial on the hill | none | `w2_d_2` | 2511 | 2,024, 2,008, 84 | `DetailObjects.Shovel02_203` |
| Bowa-Seko | the crashed glider | none | `w2_d_2` | 1628 | 1,800, 1,320, 45 | `VisualObject_31419` |
| Bowa-Seko | the bus execution | none | `w2_d_2` | 1300 | 1,338, 1,040, 15 | `Vehicle.BusBurned_628` |
| Bowa-Seko | the field of crosses | none | `w2_c_2` | 3539 | 1,227, 2,875, 28 | `StaticObject_10394` |
| Bowa-Seko | the lone water tower | none | `w2_c_2` | 2591 | 1,999, 2,061, 68 | `StaticObject_7077` (one of 2) |
| Bowa-Seko | the livestock market | none | `w2_d_2` | 1865 | 1,608, 1,528, 37 | `Buildings.Kiosk_W2_Medium_B_2` |
| Bowa-Seko | the riverside landing | none | `w2_c_3` | 3640 | 2,600, 2,920, 16 | `StaticObject_74067` |
| Bowa-Seko | the courier plane | `carvertapes` | `w2_c_4` | 3581 | 3,940, 2,829, 19 | `StaticObject_49050` (one of 2) |
| Bowa-Seko | the truck wreck with its dead | none | `w2_b_4` | 4208 | 3,120, 3,330, 45 | `StaticObject_26844` |
| Leboa-Sako | the fishing hamlet | none | `w1_b_4` | 4062 | 3,970, 3,240, 16 | `FishingVillage.fishingsmbuilding_335` |
| Leboa-Sako | the shanty settlement | none | `w1_b_3` | 4203 | 2,808, 3,352, 30 | `Furniture.Bed1_0` |
| Leboa-Sako | the broken bridge | none | `w1_c_2` | 3705 | 1,656, 2,984, 35 | `StaticObject_8851` |
| Leboa-Sako | a TV shack | none | `w1_d_4` | 2133 | 3,416, 1,672, 26 | `Appliances.TvBIG_On_0` |
| Leboa-Sako | a second TV shack | none | `w1_d_4` | 1731 | 3,320, 1,368, 59 | `Appliances.TvBIG_On_2` |
| Leboa-Sako | the lone BA4B | `carvertapes` | `w1_d_2` | 1783 | 1,498, 1,416, 34 | `StaticObject_10166` (one of 5) |

### Lightly used named places

Named places that one or two missions use. `keylocationtriggers` is the random-encounter system's
trigger, not a mission; `bsq` are buddy side quests, `a1lm`/`a2lm` faction contracts, `a2vm` a
gun-shop mission, `a3sm15` the finale.

| World | Place | Missions using it | Level | Sector | Position (x, y, z) | Map tab key |
| --- | --- | --- | --- | --- | --- | --- |
| Bowa-Seko | the warlords camp | `a3sm15` | `w2_d_4` | 1817 | 3,678, 1,472, 77 | `MajorLocation_Warlords` |
| Bowa-Seko | Heart of Darkness | `a3sm15` | `w2_d_4` | 1489 | 3,160, 1,202, 86 | `MajorLocation_MouthOfDarkness` |
| Bowa-Seko | Landing Zone | `a3sm15` | `w2_d_4` | 1578 | 3,754, 1,222, 82 | `MajorLocation_LZ` |
| Bowa-Seko | Outpost | `a2lm10` | `w2_b_3` | 4683 | 2,801, 3,760, 20 | `MajorLocation_Outpost` |
| Bowa-Seko | Swamp | `a3sm15` | `w2_d_4` | 1493 | 3,454, 1,214, 73 | `MajorLocation_Swamp` |
| Leboa-Sako | "Private Property" | `bsq06`, `a1lm04` | `w1_c_4` | 3411 | 3,325, 2,713, 17 | `MajorLocation_Villa` |
| Leboa-Sako | Claes Products | `bsq10`, `a1lm05` | `w1_d_3` | 2195 | 2,302, 1,786, 21 | `MajorLocation_W1D3_Chem_Depot` |
| Leboa-Sako | Oasis (north) | `a1lm06`, `keylocationtriggers` | `w1_b_4` | 5021 | 3,961, 3,974, 17 | `MajorLocation_3` |
| Leboa-Sako | Oasis (south-east) | `bsq13`, `a1lm04` | `w1_d_4` | 1501 | 3,937, 1,196, 30 | `MajorLocation_W1D4_DSOasis` |
| Leboa-Sako | Police Station | `bsq08`, `a1lm01` | `w1_c_2` | 3220 | 1,281, 2,620, 18 | `MajorLocation_GnCamp` |
| Bowa-Seko | M-S Pipeline | `bsq16`, `a2lm08` | `w2_d_3` | 1874 | 2,231, 1,514, 32 | `MajorLocation_Pipeline` |
| Bowa-Seko | Post Office | `a2lm09`, `a2vm05` | `w2_c_2` | 2906 | 1,688, 2,367, 26 | `MajorLocation_PostOffice` |
| Bowa-Seko | Ranger Station | `bsq18`, `a2lm07` | `w2_c_2` | 3141 | 1,387, 2,529, 23 | `MajorLocation_TouristResort` |

### Dressed scenes with no mission

All 82 hotspots that no mission touches, by world and score. The props are the four with the highest
rarity weight.

| World | Score: what is there | Level | Sector | Position (x, y, z) | Map tab key |
| --- | --- | --- | --- | --- | --- |
| Leboa-Sako | 583: `Can6` x4, `Box05` x7, `PlasticBarrel01` x4, `Can01` x4 | `w1_b_4` | 4142 | 3,992, 3,272, 18 | `DetailObjects.Box05_10` |
| Leboa-Sako | 483: `Bed1` x4, `shantymd01wall_03` x4, `Shanty.ShantyMd01Wall` x11, `shantymd01roof_01t` x3 | `w1_b_3` | 4203 | 2,808, 3,352, 27 | `Shanty.ShantyMd01Wall_471` |
| Leboa-Sako | 478: `Cage02` x4, `Box03` x4, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM05` x2, `roadsign_pole_01` x3 | `w1_d_3` | 2043 | 2,792, 1,624, 26 | `DetailObjects.Box03_8` |
| Leboa-Sako | 429: `Box05` x5, `Banana01` x2, `ShelvesMedium` x3, `ChairWood01` x4 | `w1_b_4` | 3981 | 3,944, 3,192, 20 | `CoverObjects.WoodCrateSquare_62` |
| Leboa-Sako | 374: `ChairWood01_BK` x4, `Blue_Faction.Assault_Nubian` x4, `pumpmobileblue_a` x2, `MetalBarrel02` x3 | `w1_b_3` | 4281 | 2,648, 3,432, 78 | `Blue_Faction.Assault_Nubian_4` |
| Leboa-Sako | 227: `MissionObjectiveSigns.PetroSahelSign` x3, `MissionObjectiveSigns.PoliceStationSign` x3, `MissionObjectiveSigns.ScrapSalvageSign` x3, `MissionObjectiveSigns.SlaughterhouseSign` x3 | `w1_c_2` | 3629 | 1,896, 2,920, 37 | `MissionObjectiveSigns.SlaughterhouseSign_8` |
| Leboa-Sako | 210: `shantymd01shelves_01` x3, `Burnable.CardboxSmallFull01` x3, `Shanty.ShantyMd01Wall` x3, `PlasticBarrel` x5 | `w1_d_4` | 2133 | 3,416, 1,672, 29 | `Burnable.CardboxMediumFull01_11` |
| Leboa-Sako | 185: `Quadrupeds.Gazelle` x3, `ChairPlastic05` x3, `PlasticBarrel` x3, `RustyBarrel` x2 | `w1_d_2` | 1710 | 1,960, 1,368, 35 | `CoverObjects.WoodCrateSquare_799` |
| Leboa-Sako | 157: `ChairWood01_BK` x4, `bookshelf2panes`, `TamTam01`, `fertilizerbags01` | `w1_b_3` | 4761 | 2,664, 3,800, 59 | `Furniture.ChairWood01_BK_9` |
| Leboa-Sako | 155: `CardboxBigFull01_BK.Paper` x3, `PlasticBarrel` x5, `Kiosk_W1_Small` x2, `MetalBarrel01` x3 | `w1_d_4` | 1976 | 3,640, 1,576, 19 | `DetailObjects.CardboxBigFull01_BK.Paper_10` |
| Leboa-Sako | 151: `metalbridge_mid` x5, `WoodBridge_P01_PREFAB` x7, `metalbridge_midbrk` x2, `metalbridge_base01` x2 | `w1_c_2` | 3705 | 1,656, 2,984, 35 | `StaticObject_8851` |
| Leboa-Sako | 149: `RustyBarrel` x4, `PlasticBarrel` x5, `quayconnect01`, `StreetSigns.UrbanStreetSignStop` | `w1_d_4` | 1892 | 3,384, 1,528, 16 | `CoverObjects.PlasticBarrel_50` |
| Leboa-Sako | 133: `TireMedium01` x2, `CardboxSmallFull01_BK.Paper` x2, `BeerBox01` x3, `Bottle1` x3 | `w1_b_2` | 4751 | 2,040, 3,784, 24 | `DetailObjects.TireMedium01_1` |
| Leboa-Sako | 130: `Appliances.TvBIG_On`, `bedroll_04`, `PlasticBarrel` x2, `StreetSigns.UrbanStreetSignStop` | `w1_d_4` | 1731 | 3,320, 1,368, 59 | `DetailObjects.Can02_6` |
| Leboa-Sako | 105: `roadsign_pole_01` x3, `StreetSigns.CountryStreetSignOneWay` x2, `roadsign_base_01` x3, `MissionObjectiveSigns.FortSign` x2 | `w1_b_3` | 4926 | 2,952, 3,928, 20 | `StaticObject_32949` |
| Leboa-Sako | 103: `MetalBarrel02` x5, `TolloyRuinPart3` x2, `TolloyRuinPart2`, `gazelleskull01` | `w1_b_3` | 5004 | 2,840, 4,024, 20 | `CoverObjects.MetalBarrel02_22` |
| Leboa-Sako | 97: `trainplatform01` x4, `StreetSigns.CountryStreetSignRailRoad` x2, `CheckpointBarrier01_Open_BK` x2, `StaticObject` x2 | `w1_e_2` | 1065 | 1,656, 872, 21 | `StreetSigns.CountryStreetSignRailRoad_1` |
| Leboa-Sako | 92: `sandbags01`, `shantymd01roof_01`, `shantymd01wall_03`, `generictable01` | `w1_b_2` | 4266 | 1,688, 3,400, 51 | `Shanty.ShantyMd01Wall_455` |
| Leboa-Sako | 88: `Box01` x3, `fishingsmgazebo_02`, `fishingsmroof_02`, `fishingsmbuilding_01` | `w1_b_4` | 4461 | 3,928, 3,560, 21 | `Industrial.Boxes01_476` |
| Leboa-Sako | 84: `EntitySpawner`, `shantymd01wall_02`, `shantymd01roof_03r`, `Burnable.BasketStraw02` | `w1_c_4` | 2621 | 3,928, 2,088, 39 | `Burnable.BasketStraw02_72` |
| Leboa-Sako | 84: `roadsign_pole_01` x3, `roadsign_base_01` x3, `StreetSigns.CountryStreetSignNoEntry` x2, `MissionObjectiveSigns.ClaesProductsSign` x2 | `w1_d_3` | 1878 | 2,456, 1,528, 42 | `MissionObjectiveSigns.ClaesProductsSign_3` |
| Leboa-Sako | 84: `metalbridge_end` x2, `metalbridge_mid` x2, `MissionObjectiveSigns.PoliceStationSign`, `MissionObjectiveSigns.ScrapSalvageSign` | `w1_c_2` | 3550 | 1,960, 2,824, 32 | `MissionObjectiveSigns.ScrapSalvageSign_7` |
| Leboa-Sako | 78: `Quadrupeds.Zebra` x5, `Quadrupeds.Gazelle` x4, `Animal.SavannahAnimals` x5 | `w1_b_3` | 4913 | 2,168, 3,928, 23 | `Quadrupeds.Gazelle_29` |
| Leboa-Sako | 77: `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM02`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM03`, `MissionObjectiveSigns.VeterinarianSign`, `MissionObjectiveSigns.PetroSahelSign` | `w1_b_2` | 4022 | 1,448, 3,240, 45 | `MissionObjectiveSigns.PetroSahelSign_6` |
| Leboa-Sako | 75: `Bottle1` x2, `WoodCrateSquare` x2, `fishingsmroof_02`, `WalkieTalkie01` | `w1_d_2` | 2420 | 1,288, 1,944, 23 | `CoverObjects.WoodCrateSquare_803` |
| Leboa-Sako | 74: `StreetSigns.UrbanStreetSignLimit50`, `MissionObjectiveSigns.WellandWindmillShopSign`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM04`, `StreetSigns.CountryStreetSignSinuousRoad` | `w1_c_4` | 2859 | 3,816, 2,248, 56 | `StreetSigns.UrbanStreetSignLimit50_4` |
| Leboa-Sako | 72: `potatostack`, `BBQ01`, `potatogroup01`, `ShantySm01Dest` | `w1_d_4` | 1894 | 3,480, 1,480, 16 | `Buildings.ShantySm01Dest_19` |
| Leboa-Sako | 70: `StreetSigns.UrbanStreetSignRailRoad`, `kiosk_w1_large2`, `StreetSigns.CountryStreetSignRailRoad`, `bridgetrack01` | `w1_d_3` | 1875 | 2,280, 1,496, 22 | `StreetSigns.UrbanStreetSignRailRoad_0` |
| Leboa-Sako | 69: `StreetSigns.UrbanStreetSignOneWay`, `tearoad`, `pubmertensegolo_01`, `MissionObjectiveSigns.UndertakerSign` | `w1_d_4` | 1978 | 3,736, 1,544, 21 | `StreetSigns.UrbanStreetSignOneWay_0` |
| Leboa-Sako | 67: `Quadrupeds.Gazelle` x2, `fishingsmgazebo_02`, `fishingsmroof_02`, `fishingsmbuilding_01` | `w1_d_2` | 2180 | 1,320, 1,784, 18 | `Quadrupeds.Gazelle_11` |
| Leboa-Sako | 66: `BonesRibcageWildebeest` x4, `PickupTruckWreck04` x2, `PlasticBarrelCut` x2, `StaticObject` x2 | `w1_e_2` | 1146 | 1,720, 920, 24 | `StaticObject_1000` |
| Leboa-Sako | 66: `Bottle5` x2, `Kiosk_W1_Medium`, `Can7`, `Can4` | `w1_d_2` | 1949 | 1,896, 1,560, 42 | `Buildings.ShantyShelter01_7` (one of 2) |
| Leboa-Sako | 61: `PlasticBarrel` x2, `StreetSigns.CountryStreetSignLimit50`, `bedframe01`, `DiamondBriefcase_LVL1` | `w1_d_4` | 1729 | 3,176, 1,368, 16 | `CoverObjects.PlasticBarrel_46` |
| Leboa-Sako | 60: `CardboxSmallFull02_BK.Paper`, `ShantySm01Dest`, `CardboxBigFull01_BK.Paper`, `TiresCulumns02_BK` | `w1_d_4` | 2379 | 3,816, 1,880, 21 | `DetailObjects.CardboxBigFull01_BK.Paper_13` |
| Bowa-Seko | 419: `EthiopianBasket01_BK` x4, `Kiosk_W2_Medium_B` x4, `Cage04` x6, `Cage02` x10 | `w2_d_2` | 1865 | 1,608, 1,528, 38 | `DetailObjects.EthiopianBasket01_BK_0` |
| Bowa-Seko | 389: `Can00` x3, `dogonwindow03` x2, `Can01` x3, `foodpile03` x3 | `w2_a_2` | 5940 | 1,288, 4,776, 19 | `StaticObject_119231` |
| Bowa-Seko | 387: `woodbridge_base` x5, `WoodBridge_P02` x3, `sandbagscover06` x4, `WoodBridge_P01` x7 | `w2_c_3` | 2754 | 2,216, 2,216, 32 | `Infrastructures.WoodBridge_P01_830` |
| Bowa-Seko | 382: `PlasticBarrel01` x5, `Cage04` x9, `piroguebig01_dt` x3, `LivestockCage` x10 | `w2_b_3` | 4195 | 2,280, 3,384, 16 | `StaticObject_93774` |
| Bowa-Seko | 275: `woodcross03` x17, `woodcross02` x10, `woodcross01` x14, `Awnings_Straw_Sm01_BK` | `w2_c_2` | 3539 | 1,224, 2,872, 29 | `StaticObject_10361` |
| Bowa-Seko | 239: `dzpole02` x7, `metalbridge_base01` x4, `metalbridge_base02` x4, `sign_ceasefire` x8 | `w2_c_3` | 3163 | 2,792, 2,536, 22 | `StaticObject_8805` |
| Bowa-Seko | 216: `dogonmattress01` x3, `BenchStatue01` x3, `BuildingShelterStraw01` x2, `buildingshelterpilars01` x2 | `w2_b_3` | 3958 | 2,488, 3,160, 47 | `Furniture.BenchStatue01_70` |
| Bowa-Seko | 204: `LivestockCage` x8, `Box03` x4, `benchrocksmall01` x2, `ShantyShelterW2` x2 | `w2_b_4` | 4928 | 3,128, 3,912, 34 | `StaticObject_26834` |
| Bowa-Seko | 202: `woodcross02` x5, `woodcross01` x10, `hutsm04wallwood_01` x2, `NewParticlesEffect` x4 | `w2_d_4` | 2129 | 3,144, 1,672, 22 | `StaticObject_10506` |
| Bowa-Seko | 197: `hutmdstorage01` x3, `hutsm02wallwood_01` x2, `NewParticlesEffect` x5, `Burnable.CampfireLogs` x3 | `w2_b_2` | 4743 | 1,496, 3,832, 17 | `StaticObject_9796` |
| Bowa-Seko | 174: `WoodCrateRectangular` x6, `LivestockCage` x5, `PlasticBarrel` x4, `WoodCrateSquare` x4 | `w2_c_4` | 3499 | 3,832, 2,760, 45 | `Ammo.Small_Ammo_Pickup_EX_03_1` |
| Bowa-Seko | 170: `WreckDoor01` x3, `MetalBarrel4` x5, `wreckbumper01` x2, `PlasticBarrel` x3 | `w2_b_4` | 4781 | 3,960, 3,832, 23 | `CoverObjects.MetalBarrel4_28` |
| Bowa-Seko | 169: `ruinswall02` x2, `granarysmall02` x2, `ruinswall01` x2, `pirogue02_dt` x2 | `w2_b_3` | 4846 | 2,968, 3,880, 17 | `StaticObject_106619` |
| Bowa-Seko | 157: `foodpile03` x6, `BucketWood` x3, `hutsm04wallwood_04` x2, `JungleFootPatrol` | `w2_d_4` | 2215 | 3,560, 1,768, 41 | `StaticObject_49074` |
| Bowa-Seko | 147: `foodpile03` x5, `hutsm04wallwood_04` x2, `sandbagscover06` x2, `BucketWood` x2 | `w2_d_4` | 2448 | 3,096, 1,944, 18 | `StaticObject_52734` |
| Bowa-Seko | 136: `dzpole02` x4, `sign_ceasefire` x6, `WoodBridge_P01` x9, `woodbridge_base` x3 | `w2_c_3` | 2918 | 2,472, 2,312, 23 | `Infrastructures.WoodBridge_P01_753` |
| Bowa-Seko | 131: `BeerBox01` x5, `Animal.GenericMultiUser` x2, `PlasticBarrel01` x2, `CardboxSmallFull01_BK` x2 | `w2_c_3` | 3314 | 2,184, 2,680, 19 | `DetailObjects.CardboxBigFull01_BK_92` |
| Bowa-Seko | 130: `hutsm04wallwood_04` x2, `ShantyMd01DestDoor` x2, `hutsm02roofframe_02`, `hutsm01wallwood_05` | `w2_c_2` | 3544 | 1,544, 2,840, 25 | `Furniture.ChairWood01_2231` |
| Bowa-Seko | 127: `WoodBridge_P01_PREFAB` x8, `foundation_bridge` x2, `woodbridge_base` x2, `afriqatelecom` | `w2_b_3` | 4360 | 2,584, 3,464, 21 | `StaticObject_7664` |
| Bowa-Seko | 127: `trainmetalwagondoor01` x4, `trainplatform01` x3, `trainmetalwagonopen01` x2, `Quadrupeds.Gazelle` x3 | `w2_b_2` | 4656 | 1,080, 3,768, 24 | `StaticObject_6486` |
| Bowa-Seko | 114: `MissionObjectiveSigns.SedikoSign` x2, `MissionObjectiveSigns.GeneralStoreSign` x2, `roadsign_pole_01` x3, `roadsign_base_01` x3 | `w2_b_3` | 4042 | 2,712, 3,224, 37 | `StaticObject_36288` |
| Bowa-Seko | 112: `LivestockCage` x7, `military_metalbox_small` x3, `BenchStatue01` x2, `BucketWood` x2 | `w2_b_3` | 4446 | 3,000, 3,544, 45 | `Furniture.BenchStatue01_156` |
| Bowa-Seko | 110: `Corpses.Corpse` x10, `Carver's tape 7`, `BonesRibcageWildebeest` x2, `Vehicle.BusBurned` | `w2_d_2` | 1300 | 1,336, 1,032, 16 | `Carver's tape 7` |
| Bowa-Seko | 108: `WoodBridge_P01_PREFAB` x8, `woodbridge_base` x6, `foundation_bridge` x2, `StreetSigns.ForkRoad_BK` | `w2_c_2` | 3228 | 1,816, 2,568, 17 | `StaticObject_7267` |
| Bowa-Seko | 108: `hutsm04wallwood_04` x2, `hutsm01wallwood_05`, `hutsm04wallwood_05`, `HutMd01Roof_02a_BK` | `w2_c_2` | 2820 | 1,320, 2,280, 23 | `StaticObject_112271` |
| Bowa-Seko | 106: `Shovel02` x3, `NewParticlesEffect` x5, `Burnable.CampfireLogs` x3, `Shovel01` x2 | `w2_d_2` | 2511 | 2,024, 2,008, 84 | `DetailObjects.Shovel01_72` |
| Bowa-Seko | 106: `passport_blue` x9, `passport_red` x3, `NewParticlesEffect` x4, `DiamondBiefcase_LVL1` | `w2_d_2` | 2498 | 1,208, 2,040, 42 | `VisualObject_31393` |
| Bowa-Seko | 102: `logopanel02` x4, `dzpole01` x2, `MissionObjectiveSigns.AfriQaTelecomSign`, `MissionObjectiveSigns.DentalClinicSign` | `w2_c_3` | 3236 | 2,344, 2,584, 26 | `MissionObjectiveSigns.AfriQaTelecomSign_0` |
| Bowa-Seko | 99: `BucketWood` x2, `HutSm04Roof_02a`, `hutsm04wallwood_01`, `hutsm04roofframe_02` | `w2_d_4` | 2208 | 3,112, 1,752, 17 | `StaticObject_107562` (one of 4) |
| Bowa-Seko | 98: `Cage04` x6, `Cage02` x3, `Ammo.Small_Explosive_Pickup_EX_07`, `PlasticBarrel` x2 | `w2_c_4` | 2769 | 3,176, 2,216, 21 | `DetailObjects.Cage04_20` |
| Bowa-Seko | 91: `trainplatform01` x4, `CheckpointBarrier01_Open_BK` x2, `StaticObject` x2, `MissionObjectives.TrainFuelCar` | `w2_a_2` | 6185 | 1,656, 4,968, 21 | `StaticObject_859` |
| Bowa-Seko | 90: `BonesRibcageGazelle` x7, `NewParticlesEffect` x7, `MeatBbq01` x2, `Meat05` x2 | `w2_d_2` | 1389 | 1,880, 1,144, 21 | `Buildings.ShantySm02Dest_02W2_BK_46` |
| Bowa-Seko | 86: `metalbridgeroad_mid` x4, `Winged.Birds` x3, `metalbridgeroad_end` x2, `metalbridge_base01` x2 | `w2_d_4` | 2212 | 3,384, 1,784, 70 | `Winged.Birds_6` (one of 3) |
| Bowa-Seko | 82: `WoodBridge_P01_PREFAB` x8, `foundation_bridge` x2, `woodbridge_base` x2 | `w2_b_3` | 4118 | 2,440, 3,272, 22 | `Infrastructures.WoodBridge_P01_PREFAB_13` |
| Bowa-Seko | 74: `awnings_struct_lg` x7, `pirogue02` x2, `DiamondBriefcase_LVL3`, `Corpses.Corpse` | `w2_c_3` | 2601 | 2,664, 2,088, 17 | `StaticObject_122060` (one of 2) |
| Bowa-Seko | 70: `platewood_broken_01`, `basketstrawgroup01`, `ChariotBox02_BK`, `ricebag_pile` | `w2_b_4` | 4215 | 3,560, 3,352, 54 | `StaticObject_15193` |
| Bowa-Seko | 70: `StaticObject` x27, `BonesRibcageGazelle` x2, `Land.BigTruck`, `BonesRibcageWildebeest` | `w2_a_2` | 6099 | 1,272, 4,904, 22 | `StaticObject_1124` |
| Bowa-Seko | 68: `MissionObjectiveSigns.GeneralStoreSign` x2, `roadsign_pole_01` x3, `roadsign_base_01` x3, `MissionObjectiveSigns.DogonVillageSign` x2 | `w2_b_3` | 4519 | 2,520, 3,608, 28 | `MissionObjectiveSigns.DogonVillageSign_3` |
| Bowa-Seko | 67: `BranchesShelterStraw04` x2, `Animal.SavannahAnimals` x3, `hutmd02frame_01`, `buildingshelterpilars01` | `w2_b_3` | 5075 | 2,296, 4,056, 19 | `Buildings.BranchesShelterStraw04_10` |
| Bowa-Seko | 67: `StreetSigns.UrbanStreetSignSinuousRoad`, `pubclaesprod_01`, `MissionObjectiveSigns.WeelegolVillage`, `MissionObjectiveSigns.TaemoCoSign` | `w2_d_2` | 2343 | 1,480, 1,880, 63 | `StreetSigns.UrbanStreetSignSinuousRoad_0` |
| Bowa-Seko | 66: `BonesRibcageWildebeest` x4, `PickupTruckWreck04` x2, `PlasticBarrelCut` x2, `StaticObject` x2 | `w2_a_2` | 6266 | 1,720, 5,016, 24 | `StaticObject_1000` |
| Bowa-Seko | 65: `MetalBarrel4` x6, `WoodCrateRectangular` x3, `Sea.SwampBoat`, `DiamondBriefcase_LVL2` | `w2_c_4` | 3417 | 3,688, 2,696, 17 | `CoverObjects.WoodCrateRectangular_122` |
| Bowa-Seko | 65: `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM11`, `pubclaesprod_01`, `MissionObjectiveSigns.SepokoSign`, `MissionObjectiveSigns.MosateSelaoSign` | `w2_b_4` | 4294 | 3,512, 3,416, 48 | `StaticObject_92855` |
| Bowa-Seko | 64: `Quadrupeds.Wildebeest` x5, `Animal.SavannahAnimals` x6 | `w2_d_2` | 1387 | 1,768, 1,128, 14 | `Quadrupeds.Wildebeest_4` (one of 2) |
| Bowa-Seko | 64: `StreetSigns.CrossRoadSign_BK` x4, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM11`, `MissionObjectiveSigns.RailXingSign`, `MissionObjectiveSigns.PolytechnicSign` | `w2_c_4` | 3574 | 3,496, 2,872, 35 | `MissionObjectiveSigns.RailXingSign_0` |
| Bowa-Seko | 62: `Corpses.Corpse` x2, `CarverTape_W2B4`, `WoodCrateSquare` x2, `truckwrecked01` | `w2_b_4` | 4208 | 3,128, 3,336, 45 | `StaticObject_26844` |
| Bowa-Seko | 62: `WoodBridge_P01` x6, `foundation_bridge` x2, `woodbridge_base` x2 | `w2_c_3` | 3643 | 2,776, 2,920, 20 | `StaticObject_8846` |
| Bowa-Seko | 60: `StreetSigns.CountryStreetSignRailRoad` x4, `logopanel02`, `bridgetrack01`, `urbanelectricpole01a` | `w2_c_3` | 3472 | 2,088, 2,808, 23 | `StreetSigns.CountryStreetSignRailRoad_3` |

### Safe houses, guard posts and bus stops with no mission

Places the player already passes, where a small event (a raid, an ambush, a meeting) would be found
naturally.

| World | Kind, nearest place | Level | Sector | Position (x, y, z) | Map tab key |
| --- | --- | --- | --- | --- | --- |
| Leboa-Sako | bus stop, Oasis (south-east) 424 m | `w1_d_4` | 1979 | 3,784, 1,592, 19 | `VisualObject_34126` |
| Leboa-Sako | bus stop, Oasis (north) 469 m | `w1_b_4` | 4383 | 4,040, 3,512, 23 | `VisualObject_34116` |
| Leboa-Sako | bus stop, Cattle Xing 415 m | `w1_b_2` | 4830 | 1,960, 3,896, 50 | `VisualObject_117` |
| Leboa-Sako | guard post, Fort 343 m | `w1_b_4` | 4539 | 3,816, 3,608, 29 | `DetailObjects.Box02_14` |
| Leboa-Sako | guard post, Oasis (south-east) 402 m | `w1_d_4` | 1816 | 3,624, 1,448, 41 | `CoverObjects.PlasticBarrel_57` |
| Leboa-Sako | guard post, Mokuba 376 m | `w1_d_4` | 2216 | 3,640, 1,736, 20 | `CoverObjects.Checkpoint03_BK_11` |
| Leboa-Sako | guard post, Shwasana 272 m | `w1_b_3` | 3959 | 2,504, 3,144, 16 | `StaticObject_57786` |
| Leboa-Sako | guard post, OGC greenhouses 590 m | `w1_b_4` | 4059 | 3,832, 3,208, 21 | `DetailObjects.Bottle10_8` |
| Leboa-Sako | guard post, Shwasana 326 m | `w1_b_3` | 4040 | 2,616, 3,224, 56 | `StaticObject_18022` |
| Leboa-Sako | guard post, Fort 239 m | `w1_b_4` | 4932 | 3,336, 3,928, 21 | `Industrial.Boxes01_565` |
| Leboa-Sako | guard post, Mike's Bar 252 m | `w1_d_3` | 2442 | 2,728, 1,928, 34 | `PGP.ExplosivePile_211` |
| Leboa-Sako | safe house, Airfield (Leboa-Sako) 683 m | `w1_d_2` | 1388 | 1,848, 1,112, 22 | `StaticObject_6011` |
| Leboa-Sako | safe house, Oasis (south-east) 525 m | `w1_d_4` | 1413 | 3,416, 1,128, 20 | `StaticObject_34318` (one of 6) |
| Leboa-Sako | safe house, Fort 221 m | `w1_b_4` | 4778 | 3,720, 3,832, 20 | `StaticObject_9499` |
| Leboa-Sako | safe house, Mokuba 253 m | `w1_d_3` | 2447 | 3,048, 1,944, 67 | `DetailObjects.Can7_8` |
| Leboa-Sako | safe house, Airfield (Leboa-Sako) 279 m | `w1_d_3` | 1801 | 2,664, 1,432, 21 | `Props.Sandal01_0` |
| Leboa-Sako | safe house, OGC greenhouses 482 m | `w1_c_4` | 3663 | 4,072, 2,888, 24 | `DetailObjects.CigarettePack_2` (one of 2) |
| Leboa-Sako | safe house, Lumber 274 m | `w1_c_4` | 2849 | 3,176, 2,264, 52 | `DetailObjects.Can5_27` |
| Leboa-Sako | safe house, Scrap Salvage 494 m | `w1_d_2` | 2262 | 1,432, 1,816, 17 | `StaticObject_16376` (one of 3) |
| Leboa-Sako | safe house, Mokuba 496 m | `w1_d_3` | 1807 | 3,048, 1,448, 31 | `StaticObject_33024` |
| Leboa-Sako | safe house, Oasis (south-east) 321 m | `w1_d_4` | 1900 | 3,880, 1,512, 28 | `Burnable.CampfireLogs_180` (one of 3) |
| Leboa-Sako | safe house, "Private Property" 233 m | `w1_c_4` | 3649 | 3,192, 2,904, 18 | `Blue_Faction.Assault_Caucasian_30` (one of 2) |
| Leboa-Sako | safe house, Scrap Salvage 311 m | `w1_d_2` | 2427 | 1,752, 1,944, 44 | `Blue_Faction.Assault_Nubian_SH02_2` |
| Leboa-Sako | safe house, Fresh Fish 321 m | `w1_b_3` | 4045 | 2,920, 3,208, 42 | `StaticObject_6880` |
| Leboa-Sako | safe house, Shwasana 503 m | `w1_b_3` | 4759 | 2,536, 3,784, 61 | `Blue_Faction.Assault_Caucasian_79` (one of 2) |
| Leboa-Sako | safe house, Goka Falls 327 m | `w1_b_3` | 4444 | 2,856, 3,560, 64 | `Burnable.CampfireLogs_185` (one of 2) |
| Leboa-Sako | safe house, Cattle Xing 477 m | `w1_b_3` | 4834 | 2,200, 3,848, 32 | `Blue_Faction.Assault_Caucasian_77` (one of 2) |
| Leboa-Sako | safe house, Shwasana 240 m | `w1_b_3` | 4358 | 2,488, 3,496, 20 | `Burnable.CampfireLogs_175` (one of 5) |
| Leboa-Sako | safe house, Slaughter House 336 m | `w1_c_2` | 3469 | 1,864, 2,808, 31 | `Building.SP_Int_Urban_209` |
| Leboa-Sako | safe house, Goka Falls 277 m | `w1_b_3` | 4365 | 2,936, 3,464, 38 | `housesafe10` |
| Leboa-Sako | safe house, Petrosahel (fuel depot) 304 m | `w1_b_2` | 5057 | 1,128, 4,040, 19 | `housesave3` |
| Leboa-Sako | safe house, Cattle Xing 308 m | `w1_b_2` | 3950 | 1,976, 3,192, 62 | `housesave1` |
| Bowa-Seko | bus stop, Crash Site 258 m | `w2_b_4` | 4693 | 3,448, 3,736, 34 | `VisualObject_34135` |
| Bowa-Seko | bus stop, Weelegol Village 382 m | `w2_d_2` | 1386 | 1,704, 1,096, 20 | `VisualObject_34143` |
| Bowa-Seko | guard post, Sediko 296 m | `w2_b_3` | 4282 | 2,712, 3,448, 19 | `StaticObject_36282` |
| Bowa-Seko | guard post, Airfield (Bowa-Seko) 224 m | `w2_c_4` | 3410 | 3,240, 2,728, 23 | `RR_AM_Duty.Guard_1` |
| Bowa-Seko | guard post, Dental Clinic 479 m | `w2_c_3` | 3715 | 2,296, 2,968, 20 | `DetailObjects.CardboxBigFull01_BK_107` |
| Bowa-Seko | guard post, Dogon Village 228 m | `w2_b_3` | 4598 | 2,456, 3,656, 27 | `VisualObject_6791` |
| Bowa-Seko | guard post, Ranger Station 628 m | `w2_d_2` | 2418 | 1,160, 1,944, 34 | `Wreck.CarWrecked01_BK_4` |
| Bowa-Seko | guard post, Mertens-Segolo pump house 311 m | `w2_d_3` | 2357 | 2,392, 1,864, 19 | `Buildings.JUNGLE_HutSm05WallWood_01_Part01_BK_7` |
| Bowa-Seko | guard post, Post Office 290 m | `w2_c_2` | 2750 | 1,944, 2,232, 32 | `StaticObject_6262` |
| Bowa-Seko | guard post, Mosate Selao 353 m | `w2_c_3` | 3075 | 2,248, 2,440, 21 | `BuildingAccessories.Colonial_Large_Window_BK_4` |
| Bowa-Seko | guard post, Polytechnic 263 m | `w2_b_4` | 4053 | 3,432, 3,208, 49 | `CoverObjects.MetalBarrel02_34` |
| Bowa-Seko | guard post, Sefapane 554 m | `w2_b_2` | 4579 | 1,240, 3,704, 22 | `Infrastructures.StaticOutPost01_3` |
| Bowa-Seko | guard post, Dental Clinic 251 m | `w2_c_2` | 3310 | 1,928, 2,648, 22 | `StaticObject_44690` |
| Bowa-Seko | guard post, Ranger Station 235 m | `w2_c_2` | 2899 | 1,224, 2,360, 17 | `CoverObjects.WoodCrateSquare_48` |
| Bowa-Seko | safe house, Mosate Selao 472 m | `w2_c_3` | 2913 | 2,168, 2,312, 38 | `StaticObject_104569` |
| Bowa-Seko | safe house, Outpost 272 m | `w2_b_3` | 4926 | 2,984, 3,960, 33 | `StaticObject_48763` |
| Bowa-Seko | safe house, Sefapane 558 m | `w2_b_2` | 4179 | 1,256, 3,336, 30 | `CoverObjects.MetalBarrel_57` |
| Bowa-Seko | safe house, Sediko 262 m | `w2_c_3` | 3725 | 2,904, 2,968, 55 | `DetailObjects.PlasticBowlRed01_118` |
| Bowa-Seko | safe house, Sepoko 309 m | `w2_b_4` | 4778 | 3,736, 3,816, 26 | `CoverObjects.PlasticBarrel_88` |
| Bowa-Seko | safe house, Dental Clinic 379 m | `w2_c_3` | 3634 | 2,200, 2,936, 65 | `StaticObject_74079` |
| Bowa-Seko | safe house, Dogon Village 392 m | `w2_b_3` | 4997 | 2,392, 3,976, 21 | `VisualObject_24524` |
| Bowa-Seko | safe house, Polytechnic 565 m | `w2_c_4` | 3820 | 3,864, 3,016, 32 | `CoverObjects.WoodCrateRectangular_141` |
| Bowa-Seko | safe house, Marina 409 m | `w2_d_4` | 2376 | 3,608, 1,880, 76 | `DetailObjects.Cage02_6` |
| Bowa-Seko | safe house, Taemoco Mine 488 m | `w2_d_2` | 2421 | 1,368, 1,960, 70 | `InteractiveDoors.Dogon_AnimatedDoor_North` |
| Bowa-Seko | safe house, Weelegol Village 257 m | `w2_d_2` | 1944 | 1,544, 1,560, 36 | `Burnable.CampfireLogs_192` |
| Bowa-Seko | safe house, Marina 345 m | `w2_d_4` | 2295 | 3,544, 1,848, 23 | `VisualObject_34145` |
| Bowa-Seko | safe house, Sepoko 384 m | `w2_b_4` | 4216 | 3,640, 3,336, 59 | `StreetSigns.SafeHouseSign_1` (one of 6) |
| Bowa-Seko | safe house, Sefapane 336 m | `w2_b_2` | 4104 | 1,560, 3,288, 26 | `StaticObject_9832` (one of 2) |
| Bowa-Seko | safe house, M-S Pipeline 329 m | `w2_d_2` | 1710 | 1,944, 1,352, 52 | `Blue_Faction.Assault_Caucasian_02_East` |
| Bowa-Seko | safe house, Ranger Station 311 m | `w2_c_2` | 3541 | 1,384, 2,840, 31 | `StaticObject_49265` (one of 3) |
| Bowa-Seko | safe house, Weelegol Village 231 m | `w2_d_2` | 1778 | 1,192, 1,416, 32 | `Blue_Faction.Assault_Nubian_01_West` |

### Quiet roads

The ten longest stretches per world where the road crosses no square of more than three authored
props and no mission entity stands in that square or the eight around it. The splines are short
segments, so a stretch rarely runs past 400 m; neighbouring stretches often continue the same road.

| World | Quiet stretch | Length | Level | Sector | Midpoint (x, y, z) | Map tab key |
| --- | --- | --- | --- | --- | --- | --- |
| Leboa-Sako | (1,211, 3,585) to (1,275, 3,928) | 386 m | `w1_b_2` | 4738 | 1,206, 3,783, 22 | `StaticObject_14202` |
| Leboa-Sako | (2,500, 3,961) to (2,744, 3,997) | 274 m | `w1_b_3` | 5001 | 2,627, 4,023, 20 | `VisualObject_1720` |
| Leboa-Sako | (3,591, 1,401) to (3,349, 1,344) | 270 m | `w1_d_4` | 1734 | 3,473, 1,362, 58 | `StaticObject_97981` |
| Leboa-Sako | (3,462, 3,930) to (3,701, 3,905) | 269 m | `w1_b_4` | 4936 | 3,588, 3,930, 22 | `Vehicle.BusBurned_331` |
| Leboa-Sako | (1,972, 2,302) to (1,963, 2,048) | 255 m | `w1_c_2` | 2670 | 1,967, 2,174, 40 | `StaticObject_43234` |
| Leboa-Sako | (2,875, 3,841) to (2,834, 3,649) | 236 m | `w1_b_3` | 4685 | 2,890, 3,745, 48 | `StaticObject_17993` |
| Leboa-Sako | (2,374, 3,585) to (2,553, 3,689) | 236 m | `w1_b_3` | 4519 | 2,497, 3,611, 76 | `StaticObject_12674` |
| Leboa-Sako | (2,317, 3,886) to (2,217, 3,716) | 227 m | `w1_b_3` | 4755 | 2,275, 3,794, 41 | `VisualObject_1755` |
| Leboa-Sako | (3,636, 2,004) to (3,613, 1,793) | 226 m | `w1_d_4` | 2376 | 3,639, 1,896, 22 | `StreetSigns.CountryStreetSignLimit100_0` (one of 2) |
| Leboa-Sako | (3,264, 2,337) to (3,080, 2,389) | 208 m | `w1_c_4` | 3009 | 3,170, 2,374, 38 | `StaticObject_64794` |
| Bowa-Seko | (1,833, 3,093) to (2,041, 2,805) | 399 m | `w2_c_2` | 3710 | 1,972, 2,972, 23 | `Tracks.Tracks40MCurve_288` |
| Bowa-Seko | (2,312, 2,965) to (2,400, 2,690) | 395 m | `w2_c_3` | 3558 | 2,452, 2,859, 24 | `StaticObject_68226` |
| Bowa-Seko | (2,683, 4,041) to (2,461, 3,890) | 342 m | `w2_b_3` | 4999 | 2,516, 4,015, 22 | `SpecialEventPoint_999` |
| Bowa-Seko | (1,662, 1,112) to (1,412, 1,038) | 271 m | `w2_d_2` | 1304 | 1,544, 1,044, 16 | `SpecialEventPoint_South_795` |
| Bowa-Seko | (3,373, 3,248) to (3,137, 3,228) | 246 m | `w2_b_4` | 4050 | 3,245, 3,256, 46 | `StaticObject_18159` |
| Bowa-Seko | (1,237, 2,624) to (1,176, 2,439) | 235 m | `w2_c_2` | 3138 | 1,175, 2,545, 20 | `SpecialEventPoint_13` (one of 4) |
| Bowa-Seko | (2,775, 3,096) to (2,790, 2,946) | 235 m | `w2_c_3` | 3804 | 2,844, 3,010, 42 | `Land.Rover.M2_Mounted_2` (one of 6) |
| Bowa-Seko | (2,040, 3,416) to (2,235, 3,451) | 230 m | `w2_b_3` | 4193 | 2,150, 3,390, 45 | `StaticObject_22841` |
| Bowa-Seko | (1,925, 3,567) to (2,046, 3,416) | 219 m | `w2_b_2` | 4350 | 1,967, 3,466, 38 | `Infrastructures.FenceWoodWire03_BK_3` (one of 6) |
| Bowa-Seko | (2,585, 3,388) to (2,683, 3,236) | 217 m | `w2_b_3` | 4120 | 2,597, 3,286, 37 | `SpecialEventPoint_1530` |

## Every unexplained hotspot

All 116, by world and score. "Most telling props" are the six with the highest rarity weight, with
archetype group prefixes shortened. Score is the smoothed rarity sum at the peak: above 300 is a
substantial scene, 60 to 100 a small one.

| World | Position (x, y, z) | Score | Nearest place | Most telling props |
| --- | --- | --- | --- | --- |
| Leboa-Sako | 3,992, 3,272, 17 | 583 | Fort, 688 m | `Can6` x4, `Box05` x7, `PlasticBarrel01` x4, `Can01` x4, `fishnet03` x3, `Bottle2` x4 |
| Leboa-Sako | 2,808, 3,352, 29 | 483 | Fresh Fish, 436 m | `Bed1` x4, `shantymd01wall_03` x4, `Shanty.ShantyMd01Wall` x11, `shantymd01roof_01t` x3, `shantymd01wall_02` x3, `ShantyShelter01` x5 |
| Leboa-Sako | 2,792, 1,624, 24 | 478 | Airfield (Leboa-Sako), 510 m | `Cage02` x4, `Box03` x4, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM05` x2, `roadsign_pole_01` x3, `roadsign_base_01` x3, `MissionObjectiveSigns.ClaesProductsSign` x2 |
| Leboa-Sako | 1,832, 4,008, 23 | 440 | Petrosahel (fuel depot), 437 m | `military_backpack_camo` x6, `trainpassengersidedoor01` x10, `trainwoodbridge02` x10, `trainplatform02` x3, `Corpses.Corpse` x6, `PassengerTRainGlassBK` x38 |
| Leboa-Sako | 3,944, 3,192, 19 | 429 | OGC greenhouses, 618 m | `Box05` x5, `Banana01` x2, `ShelvesMedium` x3, `ChairWood01` x4, `Bottle1` x5, `ColaBox01` x3 |
| Leboa-Sako | 2,648, 3,432, 78 | 374 | Shwasana, 352 m | `ChairWood01_BK` x4, `Blue_Faction.Assault_Nubian` x4, `pumpmobileblue_a` x2, `MetalBarrel02` x3, `StreetSigns.CountryStreetSignYield` x2, `Box02` x3 |
| Leboa-Sako | 2,344, 2,376, 24 | 367 | Pala, 268 m | `ShantySm01Dest` x4, `Can6` x6, `Table01` x5, `ChairPlastic05` x8, `BeerBox01` x8, `NewParticlesEffect` x4 |
| Leboa-Sako | 2,360, 2,312, 26 | 292 | Cock-Fights, 229 m | `introtrucktroops` x3, `concretebridge` x3, `ChairPlastic05` x4, `Table01` x3, `PalletBurnable` x3, `ShantySm01Dest` x2 |
| Leboa-Sako | 1,896, 2,920, 34 | 227 | Slaughter House, 333 m | `MissionObjectiveSigns.PetroSahelSign` x3, `MissionObjectiveSigns.PoliceStationSign` x3, `MissionObjectiveSigns.ScrapSalvageSign` x3, `MissionObjectiveSigns.SlaughterhouseSign` x3, `MissionObjectiveSigns.NorthRailyardSign` x3, `MissionObjectiveSigns.CattleXingSign` x3 |
| Leboa-Sako | 3,416, 1,672, 30 | 210 | Mokuba, 242 m | `shantymd01shelves_01` x3, `Burnable.CardboxSmallFull01` x3, `Shanty.ShantyMd01Wall` x3, `PlasticBarrel` x5, `Burnable.CardboxMediumFull01` x2, `shantymd01wall_02` x2 |
| Leboa-Sako | 1,592, 2,904, 21 | 185 | Police Station, 421 m | `Buildings/Shanty.ShantyMd01DestWindow` x3, `TireConcreteFilled` x3, `Buildings/Shanty.ShantyMd01DestWall` x2, `Chair03`, `shantymd01roof_01t`, `Buildings/Shanty.ShantyMd01DestDoor` |
| Leboa-Sako | 1,960, 1,368, 35 | 185 | Claes Products, 540 m | `Quadrupeds.Gazelle` x3, `ChairPlastic05` x3, `PlasticBarrel` x3, `RustyBarrel` x2, `ChairWood01` x2, `shantymd01roof_01r` |
| Leboa-Sako | 2,456, 2,856, 21 | 179 | Slaughter House, 268 m | `WoodBridge_P01` x7, `Red_Faction.Assault_Caucasian` x3, `foundation_bridge` x2, `Health.WaterBottle` x2, `ChairWood01` x2, `WoodBridge_P02` |
| Leboa-Sako | 1,896, 2,424, 36 | 173 | Scrap Salvage, 283 m | `bigtruck_tanker_static` x2, `MetalBarrel01` x6, `NewParticlesEffect` x5, `Faction_01.Grunt` x2, `PalletBurnable` x2, `MissionObjectiveSigns.DogFightsSign` |
| Leboa-Sako | 1,528, 2,616, 23 | 163 | Police Station, 247 m | `foodcan_05` x4, `ChairPlastic05` x3, `ShantySm01Dest` x2, `RustyBarrel` x2, `Kiosk_W1_Medium_C`, `MissionObjectiveSigns.PoliceStationSign` |
| Leboa-Sako | 2,856, 1,416, 21 | 157 | Airfield (Leboa-Sako), 400 m | `Civilians.Female_Civilian_WithDress` x4, `Civilians.Male_Civilian` x6, `Scripted.CapeBuffalo` x2, `Land.Datsun.ScriptedDatsun`, `A1SM00.Event_40_Civilians`, `ChairPlastic05` x2 |
| Leboa-Sako | 2,664, 3,800, 59 | 157 | Goka Falls, 558 m | `ChairWood01_BK` x4, `bookshelf2panes`, `TamTam01`, `fertilizerbags01`, `Bench`, `fishingsmbuilding_01` |
| Leboa-Sako | 3,640, 1,576, 19 | 155 | Mokuba, 461 m | `CardboxBigFull01_BK.Paper` x3, `PlasticBarrel` x5, `Kiosk_W1_Small` x2, `MetalBarrel01` x3, `ColaBox01` x2, `BeerBox01` x2 |
| Leboa-Sako | 1,656, 2,984, 25 | 151 | Police Station, 522 m | `metalbridge_mid` x5, `WoodBridge_P01_PREFAB` x7, `metalbridge_midbrk` x2, `metalbridge_base01` x2, `metalbridge_base02` x2, `woodbridge_base` x2 |
| Leboa-Sako | 3,384, 1,528, 16 | 149 | Mokuba, 364 m | `RustyBarrel` x4, `PlasticBarrel` x5, `quayconnect01`, `StreetSigns.UrbanStreetSignStop`, `Explosives.GasCan02_NEW`, `quaylong01` |
| Leboa-Sako | 1,624, 3,544, 28 | 147 | Cattle Xing, 270 m | `ShantyMd01DestWall` x3, `ShantyShelter01` x3, `benchrockmedium01` x2, `Blue_Faction.Assault_Caucasian` x2, `shantymd01wall_03a`, `shantymd01roof_03r` |
| Leboa-Sako | 2,040, 3,784, 24 | 133 | Cattle Xing, 333 m | `TireMedium01` x2, `CardboxSmallFull01_BK.Paper` x2, `BeerBox01` x3, `Bottle1` x3, `Bottle4` x2, `ColaBox01` x2 |
| Leboa-Sako | 3,320, 1,368, 59 | 130 | Mokuba, 513 m | `Appliances.TvBIG_On`, `bedroll_04`, `PlasticBarrel` x2, `StreetSigns.UrbanStreetSignStop`, `PlasticBarrelCut`, `Can02` |
| Leboa-Sako | 3,032, 1,240, 42 | 118 | Airfield (Leboa-Sako), 508 m | `sandbagscover09` x3, `JackalTape`, `crashed_airplane_cessna_blue`, `Land.Buggy`, `Can3`, `Can7` |
| Leboa-Sako | 1,928, 1,880, 35 | 111 | Cock-Fights, 382 m | `ShantySm01Dest` x2, `ColaBox01` x2, `Kiosk_W1_Small`, `Canteen`, `Cage03`, `Can7` |
| Leboa-Sako | 2,952, 3,928, 20 | 105 | Goka Falls, 407 m | `roadsign_pole_01` x3, `StreetSigns.CountryStreetSignOneWay` x2, `roadsign_base_01` x3, `MissionObjectiveSigns.FortSign` x2, `MissionObjectiveSigns.GokaFallsSign` x2, `MissionObjectiveSigns.ShwasanaSign` x2 |
| Leboa-Sako | 2,840, 4,024, 20 | 103 | Goka Falls, 551 m | `MetalBarrel02` x5, `TolloyRuinPart3` x2, `TolloyRuinPart2`, `gazelleskull01`, `barrelconcrete02`, `Shovel01` |
| Leboa-Sako | 1,656, 872, 21 | 97 | Airfield (Leboa-Sako), 926 m | `trainplatform01` x4, `StreetSigns.CountryStreetSignRailRoad` x2, `CheckpointBarrier01_Open_BK` x2, `StaticObject` x2, `traincabose_01`, `ruinsmedium02` |
| Leboa-Sako | 1,688, 3,400, 51 | 92 | Cattle Xing, 218 m | `sandbags01`, `shantymd01roof_01`, `shantymd01wall_03`, `generictable01`, `sofabuddy`, `Shanty.ShantyMd01Wall` |
| Leboa-Sako | 2,824, 1,496, 22 | 91 | Airfield (Leboa-Sako), 428 m | `urbanelectricpole02c` x4, `Quadrupeds.Gazelle` x2, `Vehicle.CarBurned_Fire01`, `carburned02`, `carwrecked02`, `truckwrecked01` |
| Leboa-Sako | 3,928, 3,560, 21 | 88 | Oasis (north), 416 m | `Box01` x3, `fishingsmgazebo_02`, `fishingsmroof_02`, `fishingsmbuilding_01`, `bedframe01`, `generictable01` |
| Leboa-Sako | 1,544, 3,480, 53 | 87 | Cattle Xing, 344 m | `Box01` x3, `CarverTape`, `bedroll_03`, `Blue_Faction.Sniper_Caucasian`, `fishingdock_03`, `Industrial.Boxes01` |
| Leboa-Sako | 3,416, 4,056, 22 | 84 | Fort, 307 m | `benchrockmedium01` x2, `CarverTape`, `PickupPiles.ExplosivePile.Level1`, `shantymd01wall_03`, `shantymd01roof_03`, `generictable01` |
| Leboa-Sako | 3,928, 2,088, 39 | 84 | OGC greenhouses, 620 m | `EntitySpawner`, `shantymd01wall_02`, `shantymd01roof_03r`, `Burnable.BasketStraw02`, `Land.DLC_Vehicle1_DLC1`, `generictable01` |
| Leboa-Sako | 2,456, 1,528, 37 | 84 | Claes Products, 301 m | `roadsign_pole_01` x3, `roadsign_base_01` x3, `StreetSigns.CountryStreetSignNoEntry` x2, `MissionObjectiveSigns.ClaesProductsSign` x2, `MissionObjectiveSigns.AirfieldSign` x2, `MissionObjectiveSigns.WeaponShop` x2 |
| Leboa-Sako | 1,960, 2,824, 32 | 84 | Slaughter House, 242 m | `metalbridge_end` x2, `metalbridge_mid` x2, `MissionObjectiveSigns.PoliceStationSign`, `MissionObjectiveSigns.ScrapSalvageSign`, `MissionObjectiveSigns.SlaughterhouseSign`, `MissionObjectiveSigns.CattleXingSign` |
| Leboa-Sako | 2,248, 2,584, 17 | 81 | Slaughter House, 204 m | `WoodBridge_P01` x8, `foundation_bridge` x2, `woodbridge_base` x2, `Land.Datsun` |
| Leboa-Sako | 2,168, 3,928, 22 | 78 | Cattle Xing, 522 m | `Quadrupeds.Zebra` x5, `Quadrupeds.Gazelle` x4, `Animal.SavannahAnimals` x5 |
| Leboa-Sako | 1,448, 3,240, 42 | 77 | North Railyard, 254 m | `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM02`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM03`, `MissionObjectiveSigns.VeterinarianSign`, `MissionObjectiveSigns.PetroSahelSign`, `MissionObjectiveSigns.PoliceStationSign`, `MissionObjectiveSigns.NorthRailyardSign` |
| Leboa-Sako | 1,288, 1,944, 24 | 75 | Scrap Salvage, 492 m | `Bottle1` x2, `WoodCrateSquare` x2, `fishingsmroof_02`, `WalkieTalkie01`, `Land.Buggy`, `fishingsmbuilding_02` |
| Leboa-Sako | 3,816, 2,248, 51 | 74 | OGC greenhouses, 428 m | `StreetSigns.UrbanStreetSignLimit50`, `MissionObjectiveSigns.WellandWindmillShopSign`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM04`, `StreetSigns.CountryStreetSignSinuousRoad`, `MissionObjectiveSigns.OGCSign`, `MissionObjectiveSigns.MokubaSign` |
| Leboa-Sako | 3,480, 1,480, 16 | 72 | Mokuba, 442 m | `potatostack`, `BBQ01`, `potatogroup01`, `ShantySm01Dest`, `bedframe02`, `potatobag` |
| Leboa-Sako | 1,640, 2,856, 20 | 70 | Police Station, 429 m | `TireConcreteFilled` x2, `shantymd01wall_01`, `Buildings/Shanty.ShantyMd01DestDoor`, `Burnable.BasketStraw02`, `Buildings/Shanty.ShantyMd01DestWall`, `shantymd01roof_02` |
| Leboa-Sako | 2,280, 1,496, 22 | 70 | Claes Products, 291 m | `StreetSigns.UrbanStreetSignRailRoad`, `kiosk_w1_large2`, `StreetSigns.CountryStreetSignRailRoad`, `bridgetrack01`, `TiresCulumns02_BK`, `TiresCulumns01_BK` |
| Leboa-Sako | 3,736, 1,544, 20 | 69 | Oasis (south-east), 402 m | `StreetSigns.UrbanStreetSignOneWay`, `tearoad`, `pubmertensegolo_01`, `MissionObjectiveSigns.UndertakerSign`, `StreetSigns.CountryStreetSignYield`, `MissionObjectiveSigns.OGCSign` |
| Leboa-Sako | 2,776, 1,800, 25 | 69 | Mike's Bar, 388 m | `Bottle9`, `Kiosk_W1_Small`, `Kiosk_W1_Medium`, `Can3`, `Can4`, `Bottle5` |
| Leboa-Sako | 1,320, 1,784, 17 | 67 | Scrap Salvage, 583 m | `Quadrupeds.Gazelle` x2, `fishingsmgazebo_02`, `fishingsmroof_02`, `fishingsmbuilding_01`, `Land.Buggy`, `Animal.GenericMultiUser` |
| Leboa-Sako | 1,176, 552, 25 | 67 | Airfield (Leboa-Sako), 1,493 m | `Military.SandbagsCover02_BK` x2, `BonesRibcageGazelle` x2, `PickupTruckWreck04`, `StreetSigns.CountryStreetSignNoEntry`, `StreetSigns.CountryStreetSignYield`, `PlasticBarrelCut` |
| Leboa-Sako | 1,720, 920, 24 | 66 | Airfield (Leboa-Sako), 850 m | `BonesRibcageWildebeest` x4, `PickupTruckWreck04` x2, `PlasticBarrelCut` x2, `StaticObject` x2, `truckburned01`, `BonesRibcageGazelle` |
| Leboa-Sako | 1,896, 1,560, 42 | 66 | Claes Products, 464 m | `Bottle5` x2, `Kiosk_W1_Medium`, `Can7`, `Can4`, `ShantyShelter01`, `Bottle6` |
| Leboa-Sako | 2,728, 2,712, 23 | 66 | Fresh Fish, 212 m | `roadsign_pole_01` x2, `MissionObjectiveSigns.FreshFishSign`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1BU00`, `MissionObjectiveSigns.LumberSign`, `MissionObjectiveSigns.MikesBarSign`, `MissionObjectiveSigns.ShwasanaSign` |
| Leboa-Sako | 2,680, 1,848, 21 | 65 | Mike's Bar, 310 m | `MissionObjectiveSigns.ClaesProductsSign`, `MissionObjectiveSigns.AirfieldSign`, `CardboxBigFull01_BK.Paper`, `MissionObjectiveSigns.PalaSign`, `Can6`, `Can01` |
| Leboa-Sako | 2,360, 3,592, 70 | 65 | Shwasana, 262 m | `roadsign_pole_01` x3, `roadsign_base_01` x3, `MissionObjectiveSigns.ShwasanaSign` x2, `MissionObjectiveSigns.CattleXingSign` x2, `MissionObjectiveSigns.PalaSign` x2, `truckburned01` |
| Leboa-Sako | 3,176, 1,368, 26 | 61 | Mokuba, 525 m | `PlasticBarrel` x2, `StreetSigns.CountryStreetSignLimit50`, `bedframe01`, `DiamondBriefcase_LVL1`, `bedroll_02`, `ColaBox01` |
| Leboa-Sako | 3,816, 1,880, 21 | 60 | Mokuba, 523 m | `CardboxSmallFull02_BK.Paper`, `ShantySm01Dest`, `CardboxBigFull01_BK.Paper`, `TiresCulumns02_BK`, `TiresCulumns00_BK`, `ColaBox01` |
| Bowa-Seko | 2,984, 2,536, 18 | 692 | Airfield (Bowa-Seko), 270 m | `Colonial_Large_Window_BK` x3, `TiresCulumns01_BK` x5, `Bottle3` x5, `ShantyShelterW2` x6, `ChairPlastic04` x5, `CardboxBigFull01_BK` x9 |
| Bowa-Seko | 3,608, 2,424, 16 | 450 | Rail Xing, 351 m | `Bottle12` x5, `WoodCrateRectangular` x4, `LivestockCage` x4, `D_BS_Duty.Surveilance` x2, `sandbagscover01` x3, `Cage04` x3 |
| Bowa-Seko | 2,216, 2,760, 24 | 427 | Dental Clinic, 408 m | `SorghumBasket03_BK` x4, `Burnable.BasketStraw01` x4, `Kiosk_W2_Small` x6, `bedroll_02` x5, `Kiosk_W2_Medium_B` x3, `LivestockCage` x5 |
| Bowa-Seko | 1,608, 1,528, 36 | 419 | Taemoco Mine, 257 m | `EthiopianBasket01_BK` x4, `Kiosk_W2_Medium_B` x4, `Cage04` x6, `Cage02` x10, `Burnable.StrawCage01` x3, `LivestockCage` x7 |
| Bowa-Seko | 1,288, 4,776, 19 | 389 | Sefapane, 1,319 m | `Can00` x3, `dogonwindow03` x2, `Can01` x3, `foodpile03` x3, `Ladders.LadderDogon14steps` x2, `ricebag_02` x2 |
| Bowa-Seko | 2,216, 2,216, 32 | 387 | Mosate Selao, 484 m | `woodbridge_base` x5, `WoodBridge_P02` x3, `sandbagscover06` x4, `WoodBridge_P01` x7, `benchlarge02` x3, `Blue_Faction.Assault_Caucasian` x4 |
| Bowa-Seko | 2,280, 3,384, 17 | 382 | Dogon Village, 240 m | `PlasticBarrel01` x5, `Cage04` x9, `piroguebig01_dt` x3, `LivestockCage` x10, `hutsm01wallwood_03` x2, `PlasticBowlBlue01` x3 |
| Bowa-Seko | 2,248, 2,696, 25 | 324 | Mosate Selao, 387 m | `MangoStack01_BK` x4, `PlasticBarrel01` x4, `PlasticBowlRed01` x3, `StreetSigns.CountryStreetSignRailRoad` x2, `PlasticBowlOrange01` x2, `bagfabrichutpile_01` x2 |
| Bowa-Seko | 2,008, 1,720, 62 | 281 | Taemoco Mine, 220 m | `ShantyMd01DestWindow` x4, `Burnable.HayBundle02` x2, `Awnings_Straw_Sm01_BK` x2, `storage01` x2, `Burnable.HayBundle01` x2, `WoodCrateSquare` x3 |
| Bowa-Seko | 1,224, 2,872, 29 | 275 | Ranger Station, 379 m | `woodcross03` x17, `woodcross02` x10, `woodcross01` x14, `Awnings_Straw_Sm01_BK`, `DiamondBriefcase_LVL3` |
| Bowa-Seko | 2,792, 2,536, 21 | 239 | Mosate Selao, 200 m | `dzpole02` x7, `metalbridge_base01` x4, `metalbridge_base02` x4, `sign_ceasefire` x8, `dzpole01` x5, `metalbridge_mid` x3 |
| Bowa-Seko | 2,488, 3,160, 47 | 216 | Sediko, 430 m | `dogonmattress01` x3, `BenchStatue01` x3, `BuildingShelterStraw01` x2, `buildingshelterpilars01` x2, `buildingshelter01` x2, `BeerBox01_PREFAB` x2 |
| Bowa-Seko | 3,128, 3,912, 34 | 204 | Outpost, 361 m | `LivestockCage` x8, `Box03` x4, `benchrocksmall01` x2, `ShantyShelterW2` x2, `Box01` x2, `PlasticBarrel` x2 |
| Bowa-Seko | 3,144, 1,672, 22 | 202 | Marina, 227 m | `woodcross02` x5, `woodcross01` x10, `hutsm04wallwood_01` x2, `NewParticlesEffect` x4, `BonesRibcageGazelle` x3, `BucketWood` x2 |
| Bowa-Seko | 1,496, 3,832, 16 | 197 | Sefapane, 395 m | `hutmdstorage01` x3, `hutsm02wallwood_01` x2, `NewParticlesEffect` x5, `Burnable.CampfireLogs` x3, `Burnable.CardboxBigFull01` x2, `Quadrupeds.Zebra` x2 |
| Bowa-Seko | 2,600, 2,920, 18 | 177 | Mosate Selao, 400 m | `Bottle3` x3, `military_dufflebag` x2, `GenericBox_BK` x4, `sheet01` x3, `PlateWood_02_BK` x2, `pirogue02` x2 |
| Bowa-Seko | 3,832, 2,760, 44 | 174 | Rail Xing, 429 m | `WoodCrateRectangular` x6, `LivestockCage` x5, `PlasticBarrel` x4, `WoodCrateSquare` x4, `MetalBarrel4` x2, `Ammo.Small_Ammo_Pickup_EX_03` |
| Bowa-Seko | 3,960, 3,832, 23 | 170 | Sepoko, 253 m | `WreckDoor01` x3, `MetalBarrel4` x5, `wreckbumper01` x2, `PlasticBarrel` x3, `carburned01` x2, `Health.Syrette_EX` |
| Bowa-Seko | 2,968, 3,880, 19 | 169 | Outpost, 206 m | `ruinswall02` x2, `granarysmall02` x2, `ruinswall01` x2, `pirogue02_dt` x2, `dogonmattressstraw01` x2, `benchrockbig01` x2 |
| Bowa-Seko | 3,560, 1,768, 41 | 157 | Marina, 295 m | `foodpile03` x6, `BucketWood` x3, `hutsm04wallwood_04` x2, `JungleFootPatrol`, `hutsm04roofframe_01`, `HutSm04Roof_01a` |
| Bowa-Seko | 3,096, 1,944, 17 | 147 | Bowaseko Brewing Co., 336 m | `foodpile03` x5, `hutsm04wallwood_04` x2, `sandbagscover06` x2, `BucketWood` x2, `hutsm04roofframe_01`, `HutSm04Roof_01a` |
| Bowa-Seko | 2,472, 2,312, 23 | 136 | Mosate Selao, 240 m | `dzpole02` x4, `sign_ceasefire` x6, `WoodBridge_P01` x9, `woodbridge_base` x3, `dzpole01` x3, `logopanel02` x2 |
| Bowa-Seko | 2,184, 2,680, 20 | 131 | Dental Clinic, 409 m | `BeerBox01` x5, `Animal.GenericMultiUser` x2, `PlasticBarrel01` x2, `CardboxSmallFull01_BK` x2, `CardboxBigFull01_BK` x2, `MissionObjectiveSigns.DentalClinicSign` |
| Bowa-Seko | 1,544, 2,840, 25 | 130 | Dental Clinic, 284 m | `hutsm04wallwood_04` x2, `ShantyMd01DestDoor` x2, `hutsm02roofframe_02`, `hutsm01wallwood_05`, `hutsm04wallwood_05`, `ChairWood01` x2 |
| Bowa-Seko | 2,584, 3,464, 16 | 127 | Outpost, 367 m | `WoodBridge_P01_PREFAB` x8, `foundation_bridge` x2, `woodbridge_base` x2, `afriqatelecom`, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM10`, `MissionObjectiveSigns.DogonVillageSign` |
| Bowa-Seko | 1,080, 3,768, 25 | 127 | Sefapane, 725 m | `trainmetalwagondoor01` x4, `trainplatform01` x3, `trainmetalwagonopen01` x2, `Quadrupeds.Gazelle` x3, `CardboxMediumFull01_BK` x2, `DiamondBiefcase_LVL2` |
| Bowa-Seko | 1,800, 1,416, 18 | 126 | Taemoco Mine, 296 m | `fishingdock_10` x4, `Checkpoint03_BK` x2, `sandbagscover08` x2, `PalletBurnable` x2, `PalletStack01` x2, `MetalBarrel01` x2 |
| Bowa-Seko | 1,320, 3,224, 25 | 126 | Sefapane, 556 m | `cauldronmetalhut03` x2, `NewParticlesEffect` x5, `Burnable.CampfireLogs` x3, `cauldronmetalhutcover03`, `cauldronmetalhut01`, `CarverTape` |
| Bowa-Seko | 3,976, 3,352, 28 | 114 | Sepoko, 231 m | `Animal.SavannahAnimals` x10, `Quadrupeds.Zebra` x3, `benchrockmedium01` x2, `Burnable.CampfireLogs` x2, `bedroll_03`, `Ammo.Small_Fuel_Pickup` |
| Bowa-Seko | 2,712, 3,224, 38 | 114 | Sediko, 200 m | `MissionObjectiveSigns.SedikoSign` x2, `MissionObjectiveSigns.GeneralStoreSign` x2, `roadsign_pole_01` x3, `roadsign_base_01` x3, `MissionObjectiveSigns.DogonVillageSign` x2, `MissionObjectiveSigns.MosateSelaoSign` x2 |
| Bowa-Seko | 3,000, 3,544, 46 | 112 | Crash Site, 292 m | `LivestockCage` x7, `military_metalbox_small` x3, `BenchStatue01` x2, `BucketWood` x2, `military_metalbox_big` x2, `Health.WaterBottle` x2 |
| Bowa-Seko | 1,336, 1,032, 16 | 110 | Weelegol Village, 317 m | `Corpses.Corpse` x10, `Carver's tape 7`, `BonesRibcageWildebeest` x2, `Vehicle.BusBurned`, `Land.Datsun`, `busburned` |
| Bowa-Seko | 1,816, 2,568, 21 | 108 | Post Office, 239 m | `WoodBridge_P01_PREFAB` x8, `woodbridge_base` x6, `foundation_bridge` x2, `StreetSigns.ForkRoad_BK` |
| Bowa-Seko | 1,320, 2,280, 23 | 108 | Ranger Station, 258 m | `hutsm04wallwood_04` x2, `hutsm01wallwood_05`, `hutsm04wallwood_05`, `HutMd01Roof_02a_BK`, `hutmd01roofframe_02`, `hutsm04roofframe_01` |
| Bowa-Seko | 2,024, 2,008, 84 | 106 | Taemoco Mine, 379 m | `Shovel02` x3, `NewParticlesEffect` x5, `Burnable.CampfireLogs` x3, `Shovel01` x2, `Corpses.Corpse` x2, `Land.JeepWrangler` |
| Bowa-Seko | 1,208, 2,040, 42 | 106 | Ranger Station, 521 m | `passport_blue` x9, `passport_red` x3, `NewParticlesEffect` x4, `DiamondBiefcase_LVL1` |
| Bowa-Seko | 2,344, 2,584, 24 | 102 | Mosate Selao, 256 m | `logopanel02` x4, `dzpole01` x2, `MissionObjectiveSigns.AfriQaTelecomSign`, `MissionObjectiveSigns.DentalClinicSign`, `StreetSigns.CountryStreetSignSinuousRoad`, `MissionObjectiveSigns.SefapaneSign` |
| Bowa-Seko | 3,112, 1,752, 17 | 99 | Marina, 298 m | `BucketWood` x2, `HutSm04Roof_02a`, `hutsm04wallwood_01`, `hutsm04roofframe_02`, `hutsm04wallwood_02`, `hutsm04pilars_01` |
| Bowa-Seko | 1,096, 2,232, 31 | 98 | Ranger Station, 416 m | `courierpouch02`, `SorghumBasket01_BK`, `welldogon01`, `SorghumBasket02_BK`, `BuildingShelterStraw01`, `buildingshelter01` |
| Bowa-Seko | 3,176, 2,216, 16 | 98 | Airfield (Bowa-Seko), 298 m | `Cage04` x6, `Cage02` x3, `Ammo.Small_Explosive_Pickup_EX_07`, `PlasticBarrel` x2, `Equipment.Cages02`, `Burnable.StrawCage01` |
| Bowa-Seko | 1,656, 4,968, 21 | 91 | Sefapane, 1,424 m | `trainplatform01` x4, `CheckpointBarrier01_Open_BK` x2, `StaticObject` x2, `MissionObjectives.TrainFuelCar`, `traincabose_01`, `ruinsmedium02` |
| Bowa-Seko | 1,880, 1,144, 19 | 90 | Weelegol Village, 509 m | `BonesRibcageGazelle` x7, `NewParticlesEffect` x7, `MeatBbq01` x2, `Meat05` x2, `ShantySm02Dest_02W2_BK`, `DiamondBriefcase_LVL2` |
| Bowa-Seko | 3,384, 1,784, 23 | 86 | Marina, 221 m | `metalbridgeroad_mid` x4, `Winged.Birds` x3, `metalbridgeroad_end` x2, `metalbridge_base01` x2, `metalbridge_base02` x2 |
| Bowa-Seko | 3,944, 2,824, 19 | 84 | Rail Xing, 477 m | `Animal.SavannahAnimals` x6, `courierpouch` x2, `Quadrupeds.CapeBuffalo` x2, `CarverTape_W2C4`, `crashed_airplane_cessna_blue`, `WalkieTalkie01` |
| Bowa-Seko | 2,440, 3,272, 18 | 82 | Dogon Village, 405 m | `WoodBridge_P01_PREFAB` x8, `foundation_bridge` x2, `woodbridge_base` x2 |
| Bowa-Seko | 3,400, 2,776, 24 | 80 | Polytechnic, 227 m | `metalbridgeroad_mid` x3, `metalbridgeroad_end` x2, `metalbridge_base01` x2, `metalbridge_base02` x2, `sandbagscover09` x2, `sandbagscover01` |
| Bowa-Seko | 2,664, 2,088, 17 | 74 | Bowaseko Brewing Co., 351 m | `awnings_struct_lg` x7, `pirogue02` x2, `DiamondBriefcase_LVL3`, `Corpses.Corpse` |
| Bowa-Seko | 1,800, 2,648, 26 | 71 | Dental Clinic, 231 m | `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM07`, `MissionObjectiveSigns.CallingCardandPhoneRechargingStandSign`, `MissionObjectiveSigns.DentalClinicSign`, `MissionObjectiveSigns.RangerStationSign`, `MissionObjectiveSigns.PostOfficeSign`, `MissionObjectiveSigns.SefapaneSign` |
| Bowa-Seko | 3,560, 3,352, 53 | 70 | Crash Site, 323 m | `platewood_broken_01`, `basketstrawgroup01`, `ChariotBox02_BK`, `ricebag_pile`, `StreetSigns.SafeHouseSign`, `StreetSigns.ForkRoad_BK` |
| Bowa-Seko | 1,272, 4,904, 24 | 70 | Sefapane, 1,444 m | `StaticObject` x27, `BonesRibcageGazelle` x2, `Land.BigTruck`, `BonesRibcageWildebeest` |
| Bowa-Seko | 2,520, 3,608, 23 | 68 | Dogon Village, 289 m | `MissionObjectiveSigns.GeneralStoreSign` x2, `roadsign_pole_01` x3, `roadsign_base_01` x3, `MissionObjectiveSigns.DogonVillageSign` x2, `MissionObjectiveSigns.SefapaneSign` x2, `MissionObjectiveSigns.MosateSelaoSign` x2 |
| Bowa-Seko | 2,296, 4,056, 19 | 67 | Dogon Village, 442 m | `BranchesShelterStraw04` x2, `Animal.SavannahAnimals` x3, `hutmd02frame_01`, `buildingshelterpilars01`, `buildingshelter01`, `Land.Buggy` |
| Bowa-Seko | 1,480, 1,880, 65 | 67 | Taemoco Mine, 351 m | `StreetSigns.UrbanStreetSignSinuousRoad`, `pubclaesprod_01`, `MissionObjectiveSigns.WeelegolVillage`, `MissionObjectiveSigns.TaemoCoSign`, `MissionObjectiveSigns.PostOfficeSign`, `MissionObjectiveSigns.WeaponShop` |
| Bowa-Seko | 1,720, 5,016, 24 | 66 | Sefapane, 1,468 m | `BonesRibcageWildebeest` x4, `PickupTruckWreck04` x2, `PlasticBarrelCut` x2, `StaticObject` x2, `truckburned01`, `BonesRibcageGazelle` |
| Bowa-Seko | 3,688, 2,696, 16 | 65 | Rail Xing, 436 m | `MetalBarrel4` x6, `WoodCrateRectangular` x3, `Sea.SwampBoat`, `DiamondBriefcase_LVL2`, `MetalBarrel02`, `WoodCrateSquare` |
| Bowa-Seko | 3,512, 3,416, 50 | 65 | Crash Site, 249 m | `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM11`, `pubclaesprod_01`, `MissionObjectiveSigns.SepokoSign`, `MissionObjectiveSigns.MosateSelaoSign`, `MissionObjectiveSigns.WeaponShop`, `StreetSigns.ForkRoad_BK` |
| Bowa-Seko | 1,768, 1,128, 15 | 64 | Weelegol Village, 416 m | `Quadrupeds.Wildebeest` x5, `Animal.SavannahAnimals` x6 |
| Bowa-Seko | 3,496, 2,872, 32 | 64 | Polytechnic, 224 m | `StreetSigns.CrossRoadSign_BK` x4, `MissionObjectiveSignsSafeHouse.SafeHouse_A1LM11`, `MissionObjectiveSigns.RailXingSign`, `MissionObjectiveSigns.PolytechnicSign`, `MissionObjectiveSigns.SepokoSign`, `roadsign_pole_01` |
| Bowa-Seko | 3,128, 3,336, 44 | 62 | Sediko, 240 m | `Corpses.Corpse` x2, `CarverTape_W2B4`, `WoodCrateSquare` x2, `truckwrecked01`, `Appliances.BoomBoxRadio`, `Winged.Birds` |
| Bowa-Seko | 2,776, 2,920, 20 | 62 | Sediko, 338 m | `WoodBridge_P01` x6, `foundation_bridge` x2, `woodbridge_base` x2 |
| Bowa-Seko | 2,088, 2,808, 23 | 60 | Dental Clinic, 271 m | `StreetSigns.CountryStreetSignRailRoad` x4, `logopanel02`, `bridgetrack01`, `urbanelectricpole01a`, `dzpole01` |

## Open questions

- **How far does the overlap strip run?** Two scenes were matched between the worlds, the desert
  shelter and the broken railway. A full comparison of the two edges was not made.
- **Are the unexplained scenes reachable?** Most sit in the always-on `main` layer, but whether each
  one streams in where the player can walk was not checked, and two of the copies above lie outside
  the playable square.
- **What is `A1BU08`?** A scene object named `A1BU08.A1BU08` stands by the smoking Cessna in
  Leboa-Sako and in the lived-in train in Bowa-Seko; the buddy-unlock mission it names was not read.
- **Does the score miss quiet scenes?** A site of a few rare props scores low, and the glider (11
  entities) was found only by the landmark pass. Scenes made of common props, such as a single corpse
  under a tree, rank below the 60-point cut and are not listed.
