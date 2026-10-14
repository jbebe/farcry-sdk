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
