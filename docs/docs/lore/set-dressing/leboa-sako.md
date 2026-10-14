---
sidebar_position: 3
---

# Leboa-Sako

The northern map, `world1`. The journal calls it Leboa-Sako (`Objectives/A1LM00_02`); one subtitle
spells it Leboa-Seko (`4558791`). Its centre is the town of Pala, held by both factions under a
ceasefire. The UFLL papers the north-west and the APR the east and south, and the violence is mostly
old: burned cars and bones rather than fresh blood, of which the whole map has 13 decals.

Places run roughly north-west to south-east. Positions and internal names are in the
[gazetteer](./reading-the-world.md#leboa-sako-world1); counts are within 150 m of each place's marker.

:::info[Read from the retail world data]
`world1` sector and landmark files, decoded with `jackall-cli fcb decode`. *Implied* marks a reading;
everything else is a placement. Nothing here was checked in a running game.
:::

## Pala

The densest place on the map: 4,345 entities within 150 m.

**Two HQs in borrowed buildings, a block apart.** The UFLL sits in the west of town in
`hq_busstation`, its office furnished from `World1_BusStation.*` (a `DeskWarlord`, colonial chairs, a
round table). The APR sits about 90 m east in `hq_bordel`, furnished from `World1_Bordello.*`: four sofas,
four two-seat chairs and Kouassi's own pistol, `World1_Bordello.DesertEagle_Kouassi`. The journal
confirms both: "the former bordello where Prosper Kouassi … has his headquarters" and "the abandoned
bus station where Leon Gakumba … has his office" (`Objectives/A1LM00_02`, `A1LM00_03`). Each HQ is
papered in its own faction's posters (37 UFLL around one doorman, 26 APR around the other).

**Between them, the hotel and the church.** `urbanhotel` with its lobby, four rooms and an
`urbanhotel_rubblepile01` stands between the HQs. One room holds `World1_HotelRoom.HotelRoom_Photo`
seven times, `HotelRoom_BriefCaseMoney` and `HotelRoom_HandGun`. The church (`UrbanChurch`) has a
`World1_Church.ChurchShrine` and `GRIN.Father_Maliya`.

**A town under a truce.** 14 ceasefire stencils with DZ poles, fences and faction logo boards stand in
about nine clusters 90 to 160 m out, on the roads in. Sandbag walls are everywhere
(`sandbagscover09` x32, `sandbagscover10` x26), with `SpecialBarricadeForIntro_*` pieces built for
the opening.

**Poor, busy and dirty.** 182 `trashborder01` and hundreds more trash pieces, 34 `laundry01` lines,
cigarette packs, beer, 13 `tablemarket01_multi` market tables and eight kiosks. Flies over 29 spots.
11 `Wreck.CarBurned01_BK`.

**Its violence is staged, not left lying.** The town's fight in the opening is the layer `a1sm01`
(100 entities) with named fire triggers around both HQs (`A1SM01_burn_NofAPRhq`,
`A1SM01_burnExplo_NofUFLLhq`) and 24 reinforcement points per faction. Its interiors exist twice:
`w1c3_broken_interior` and `a1sm01_cleaninterior`. Only one `Corpses.Corpse` lies in town.

*Implied:* a provincial town split between two militias who took over its public buildings, still
trading and still living under a ceasefire that both sides police from checkpoints.

## Around Pala

**Mike's Bar.** A mercenaries' armoury with a bar: 31 `weaponhook_*` racks, about 60 `ammobox_*` for
every weapon class, `Primary.Fake_AK47`, `Fake_Ithaca`, `HandToHand.Fake_Machete`, cork boards with
papers. Ten AI points, none of them guard duty. The layer `a1sm03_mikesattack` is the attack the journal
announces (`Objectives/A1SM03_03`).

**Lumber.** 27 `woodpile_01`, 43 `trunkcover_*`, branch stacks and five `tractortrailer`. APR posters.
Reuben hides here (`A1SM03_ReubenGreet_ProxTrigger`; "that deserted lumber camp southeast of the
town", `Objectives/A1SM03_01`). Its guards eat and smoke (`Rest.EatMultiUserRest`, `Rest.Smoke` x12).

**Cock-Fights.** The sign reads "Cock-Fights"; the texture is `dogfights_m.xbt`. 27 rice bags, food
piles, 15 animal cages (`Cage04`, `Cage02`), kiosks, seven burned cars and nine APR posters. 21
`Rest.Lean` and the beer-crate routines: a place where soldiers stand around and drink.

**Slaughter House.** Meat on every surface: `Meat04` x19, `cutmeat04` x13, `MeatCarcassHook` x13,
`meatstack01` x10, eleven `SlaughterBuggy` carts, slaughter tables, livestock cages, and flies over 16
spots including `SoundPoint_flies_outside`. A signpost cluster points to Pala, North Railyard, Cattle
Xing, Petrosahel, Mike's Bar, the dog fights and the veterinarian. UFLL posters.

**Fresh Fish.** Fishnets, baskets, rods, two swamp boats and a `fish_state` prop: a working fishing
camp. It also has a `DunkCageOffice` and `ReinforcementPoint_DunkCage`, whose purpose the data does
not say. UFLL posters.

## The north-west: UFLL country

**Police Station** (`gncamp`). 48 `trash04`, field manuals (`fieldmanualmnt` x5), a `PassportBriefcase`
and UFLL posters. Its AI is all leisure: 22 `Social.RandomConversation`, 14 `Rest.Smoke`,
`Social.Boxing`, `Rest.SitOnGroundAndBoxing`, `Layered.SlapInsects`. *Implied:* a police post turned
militia barracks.

**Scrap Salvage.** The dump of the war: 84 wrecks within 200 m, among them 25 `carburned01`, 12
burned buses and 11 burned trucks, plus derailed passenger carriages, a locomotive, containers, giant
junk piles, and 17 blank billboards. The guards cough, yawn and slap insects (`Layered.Cough` x8,
`Layered.Yawn` x6, `Layered.SlapInsects` x13).

**Cattle Xing** (`ranch`). 51 `fencewood05`, hay, chickens (`Quadrupeds.ChickenA`), a cauldron, and
broken shanty walls (`shantymd01destwall_01_support` x9). 38 of its entities are the layer
`benchmark`: the `Bench_RelayTrigger_*` and `Bench_Explosive` points of the PC benchmark's
fly-through.

**North Railyard.** 95 panes of `PassengerTRainGlassBK`, platforms, track, two locomotives, rubble
(`trainruble_*`). UFLL posters.

**Petrosahel.** The fuel depot: gas tanks and a tank tower, propane tanks, a rail spur, 37 industrial
fence sections, 18 concrete-filled tyres, 14 UFLL posters.

**Shwasana.** A fishing village on the lake, with docks, nets, pirogues, cages and stilt houses from
the `fishingvillage` kit. Its soldiers fish (`Duty.Fishing` x11) and stand guard (`Duty.Guard` x20) in
the villagers' houses, under 22 UFLL posters. A line of dialogue puts the police chief's brother here
(`4933190`).

*Implied:* the UFLL holds the infrastructure of the north-west, the railway, the fuel and the lake, and
lives in what it took.

## The north-east

**Goka Falls** (`fool_errand`). A lodge built from the tourist-resort kit (`fencetouristresort*`,
`ChairsTouristResort01_BK`), with spears and bunk beds. Both factions' posters. The layers are the
set-up: `a1sm02_bluetarget` (21), `a1sm02_redtarget` (18), `a1sm02_blueambush` (7),
`a1sm02_redambush` (6). Whichever leader the player is sent to kill, the other side's ambush is
waiting.

**Fort** (`old_fort`). An APR barracks inside the old fort's walls (`forttowertriple`): 28 APR
posters and three graffiti, rifle racks, five `targetman06` shooting targets, potato and rice sacks.
Two things stand out: 56 piles of dung (`dung01`, `dung02`), and 42 books with bookshelves. One
`Social.Oratory` point: someone gives speeches here.

**Oasis** (`king_oasis`). Ancient ruins (`Ethiopian_RuinsWall*` x20, `ruins_large`), barrel fires,
skulls, eight `Faction_01.Grunt`, and a gold bar in the layer `a1lm06\misnbase_gold`.

## The east

**"Private Property"** (`villa`). The richest interior on the map: eight `Appliances.TvPortable`, 13
boom boxes, 13 framed photographs (`photo_01`/`03`/`04`), African axes and talking drums
(`AfricanAxe` x7, `TamTam01`/`02`), Ethiopian baskets, six `RadioTransmitter03`, behind colonial
fences, sidewalks and a concrete bridge. 19 APR posters. It is the informant's villa of
`Objectives/A1LM04_03`. *Implied:* a wealthy man's house, kept in quotation marks on the map because
everyone knows whose it is.

**OCG greenhouses.** A foreign agribusiness under guard: 26 greenhouse tables, 42 `PlantSpurs`, 13
fertiliser sacks, hedge cutters and garden claws, two `WatchTowerLeipzig` towers, and the company's
own two billboards (`pubogc_02`). APR posters.

## The south and south-east: APR country

**Mokuba.** A shantytown: about 100 pieces of the `shantymd01` kit, 11 `BigTrashPile01`, tyre walls,
12 barrel fires, 14 `sofabuddy` sofas and 11 `ComfyBed`s in the shacks, and a few kiosks. Checkpoints and
outposts close the roads (six `Checkpoint03_BK`, six `StaticOutPost01`), 15 APR posters and graffiti
cover the walls. Four `Corpses.Corpse` lie in the layer `a1lm04\misnsubv_shantytown`, with medicine
crates (`MedecineSuppliesBig00`), the commandos' trap and the APR medicine destroyed with them
(`Objectives/A1LM04_05`). An `UndertakerSign` in town points south.

**The undertaker and the hilltop cemetery,** 630 m south of Mokuba at (3,297, 1,252) and not a named
place. 25 tombstones and 18 wooden crosses, six shovels (`Shovel01`, `Shovel02`), two hoes
(`Houe01`), and two `Corpses.Corpse` above ground. A small shop interior beside it has a
`CashRegister01` and a warlord's desk, and holds the Underground layer
`grinmissions\mission_04\operative`. Dialogue sends the player "half a mile south of Mokuba
shantytown. There you'll find an undertaker's shop" (`4889730`). *Implied:* the only cemetery in
Leboa-Sako still being dug.

**Claes Products.** A chemical depot on a rail spur: 12 barrels of `DetailObjects.OldDefoliant`, plastic
and metal drums, a broken wagon. Its billboard shows frogs. The guards box and exercise
(`Social.Boxing` x9, `Rest.ExerciseMultiUserRest` x5). APR graffiti.

**Airfield.** Hangar walls, a sofa, a guitar, a boom box, a walkie-talkie, a crashed yellow Cessna.
The same boredom: `Social.Boxing` x5, `Rest.ExerciseMultiUserRest` x8, `Rest.Piss` x7.

**Oasis** (south-east, `w1d4_dsoasis`). The foreign commandos' camp of `Objectives/A1LM04_01`: four
`Special.SpecOps_Shotgun`, four `SpecOps_Assault`, a `RadarJammer`, a `RadioTransmitter03`, a
`DisabledRover01`, and 50 entities in the layer `a1lm04\misnbase_airdrop`. Sandbags, a burning car
(`Vehicle.CarBurned_Fire01`) and wildebeest bones.

## The open country

**Guard posts and checkpoints** sit at junctions, not at places: the checkpoints cluster on the roads
around Mokuba and in the hills north-east of Shwasana (seven `Checkpoint03_BK` at (2,665, 3,627)). Each has
signposts to Pala, Goka Falls, the Fort and Shwasana, sandbags, a mounted M249, a Land Rover and a
generator. 31 `MissionObjectiveSigns.PalaSign` stand in the countryside, all pointing to town.

**Wrecks** are the land's commonest scar: 163 outside the named places, burned and unburned cars,
trucks and eleven buses.

**The road south out of the map.** In the far south-west, by the world exit (`Objectives.ExitToWorld2_*`
at (1,170, 541)), a truck with three `Corpses.AnimatedCorpse` stands in the layer
`a2sm04_se01_truck_and friends`: the truck hauling the dead "out to some mass grave in the desert"
(`Objectives/A2SM04_APR_01`), with the player among them. No grave is placed. Further east, the layer
`a2sm05` sets a burning convoy wreck on the road at (1,850, 1,720 to 1,788): burn emitters, wreck
doors and wheels, burning tyres, and four corpses (`a2sm05_ai`).

*Implied:* by the time the player leaves, Leboa-Sako's dead are hauled away, not buried where they
fell; the blood is in the next map.
