---
sidebar_position: 4
---

# Bowa-Seko

The southern map, `world2`; the journal calls it Bowa-Seko (`Objectives/A2LM00_02`). Its centre is
Mosate Selao, the capital, held by both factions under a ceasefire like Pala. The APR papers the north
and east, the UFLL the south and west, around Lake Segolo. Where Leboa-Sako's violence is old,
Bowa-Seko's is fresh: 286 of the world's 299 blood decals and 68 of its 105 corpses are here.

Positions and internal names are in the
[gazetteer](./reading-the-world.md#bowa-seko-world2); counts are within 150 m of each place's marker.

:::info[Read from the retail world data]
`world2` sector and landmark files, decoded with `jackall-cli fcb decode`. *Implied* marks a reading;
everything else is a placement. Nothing here was checked in a running game.
:::

## Mosate Selao

The capital, 4,144 entities within 150 m. Every line of dialogue calls it Port Selao; its sign and map
tile say Mosate Selao.

**The same plan as Pala.** The UFLL HQ is on the west side, furnished from `World2_SocialClub.*`; the
APR HQ about 75 m east, furnished from `World2_NewsPaper.*` around two printing presses
(`prinitingpress`) and a `DeskWarlord`. *Implied:* the APR runs the town from a newspaper office.
Each HQ is papered in its own posters, and the town as a whole almost exactly evenly: 56 APR, 54 UFLL.
South of the gap between them stands the colonial church (`colonialchurch_01`, `PortCity_BellRinger`) with its
churchyard of 22 tombstones and 6 wooden crosses.

**A colonial city run down.** The `colonialmd01` building kit with balconies and shutters, about 140
sidewalk pieces, rows of colonial street lamps, 130 `colonialfence_b03` sections. And over it:
236 `trashground02`, 73 `TrashPileGroup01`, 47 `ShantyShelterW2` lean-tos, 24 burned cars, flies over
51 spots, the most of any place.

**A market and a truce.** 92 kiosk and awning pieces, the largest market in either world, and
`DetailObjects.ZuluShield01_BK` seven times. 33 ceasefire stencils stand in about ten checkpoint
clusters 120 to 250 m out.

**States.** The layer `a2sm06_capitalchaos` holds the fighting that breaks out over the Jackal's guns
(`Objectives/A2SM06_APR_05`): `Car_Fire`, `Building_Fire`, `A2SM06_Burn.BurnEmitter` and a delayed
church fire (`DelayTrigger_Church_Fire_Start`). A whole after-state,
`leveldesign\w2c3\a10_capital\post_hornestnest`, adds 646 entities.

## The north: APR country and the Dogon

**Sefapane** (`crossroads`). "Town's split down the middle between the factions"
(`Objectives/A2SM06_APR_01`). The two HQ doormen stand 60 m apart, APR at (1,752, 3,566) and UFLL at
(1,812, 3,570), but only the APR side is papered (18 APR posters within 45 m of its doorman, 2 UFLL
around the other). A colonial crossroads town with a rail line, 20 kiosks, 19 faction logo boards, 7
ceasefire stencils, a small cemetery of four stones, and a UFLL store selling a cell-phone detonator
(`World2_Store.CellDetonator_UfllStore`).

**Sediko** (`dogonring`). A Dogon village, ringed, with granaries, straw roofs, pottery, animal traps
(`trapdogon01` x9) and a jail built into it (`buddyjaildoor` x3, `buddyjailwindow` x2). In its
`a2sm08_dogonring_trashed` state it is the worst scene in the game: within 150 m, 47 drag trails, 37
blood pools, 26 hand trails and 25 small trails of blood, 15 `Corpses.Corpse` and ten
`A2SM08_Burn.BurnEmitter` fires. The story places Tambossa here for the peace delivery and later as
his hideout (`Objectives/A2SM08_UFLL_02`, `A3SM11_UFLL_02`). *Implied:* what the Jackal leaves behind
when the peace deal is murdered. Six APR posters.

**Dogon Village.** The most fully realised village: Tolloy houses and 24 Tolloy granaries, straw roofs
and stairs (82 `stairssetstraight_01`), about 30 pieces of pottery, mango stacks, chicken nests,
eroded walls and ruins. Its soldiers farm (`Duty.CultivateField` x9) and drink. No faction poster
within 200 m. The kiln of `Objectives/A2LM10_01` is here.

**Outpost.** A hut settlement on the river: the `hutmd01` kit, nine carved statue benches
(`BenchStatue01`), cooking pots, a mortar and pestle for grain, pirogues, laundry. Its routines are
domestic (`Duty.PeelPotatoes`, `Duty.KitchenPatrolA`) next to a `Duty.MolotovPileInteraction`. APR
posters.

**Crash Site.** Three `MissionObjectives.500lbBomb`, helicopter parts, sandbag positions, and 22
`Duty.CultivateField` points among 22 `Animal.SavannahAnimals`: soldiers working the fields around
the wreck. *Implied:* the salvage site of the Hind's bomb (`Objectives/A2LM11_03`). 260 of its entities
are the layer `a2lm11\misnsubv_crashsite`.

**Sepoko** (`borderpost`). A fenced border post: 68 `industrialfence_a01` and about 70 security fence
pieces, checkpoint barriers open and closed, two watchtowers and barbed wire. Its goods are contraband:
18 `CigaretteCartoon_BK`, cola, three blue passports (`passport_blue`). The guards play awale
(`Social.Awale` x11) and dice, and farm (`BSQ22_Duty.CultivateField` x13). A burned bus by the gate.

## The west

**Ranger Station** (`touristresort`). A lakeside tourist resort taken over: 118 resort fence sections,
25 resort chairs, docks, six parasols, barbecues, a bird cage, 20 Ethiopian baskets, and two `Mk19`
grenade launchers in sandbags. 29 `Duty.Guard` points. *Implied:* the tourists left; the label names
the post that replaced them.

**Post Office.** Concrete barriers, eight burned cars, burning gas pumps
(`Burn.BurnEmitterGasPump`) and three `PartnerMissionEnvelope` pickups. A signpost hub for the Ranger
Station, Sefapane, the Dental Clinic, Mosate Selao, Weelegol, TaeMoCo and a hardware store. The
predecessor's informants leave messages for him here, "One kilometer to the west of Port Selao"
(`5124224`).

**Dental Clinic** (`hutvillage`). A jungle hut village from the `hutmd04`/`hutsm05` kits, with
cauldrons, hay, gas bottles, and soldiers sleeping in its beds. No medical prop within 200 m. The
label is the only clinic there is.

## The east: the river and the airfield

**Rail Xing** (`armsbazaar`). Yabek's arms barge "on the Noga River, just under the big rail span"
(`Objectives/A2LM11_01`). The span is shipped already fallen, as the layers
`staticfallenbridgepieces` (132) and `misnsubv_wreckedbridgebargepieces` (144), with
`NewParticlesEffect_A2LM11_BridgeExplosion`. About 80 sandbag walls, three `M2` machine guns, APR
posters.

**Airfield.** Hangars, a windsock, two crashed white Cessnas, wrecked cars. Five of the world's 30
placed civilians are here (`Civilians.Male_Civilian`, `Female_Civilian`), with the soldiers' `Duty.Loot`
x4 and `Duty.Welding`. Reuben waits in the hangar (`Objectives/A3SM14_01_NBM_01`). *Implied:* people
gathering to leave by air.

**Polytechnic.** A school turned barracks: five blackboards, 15 classroom tables, the
`colonialschoolroof03`, 48 sandbag walls, soldiers sleeping on the ground (`Rest.SleepOnGround` x9)
and farming outside (`Duty.CultivateField` x17). The C-130's exhaust (`NewParticlesEffect_A2LM12_C130_Exhaust`)
for the flight mission.

## The south: UFLL country and its industry

**Taemoco Mine.** Soldiers dig for diamonds: 16 `Duty.DiamondSearch` points, two sieves (`tamis01`),
dirt piles, two small safes (`safesmall`). The mine exists flooded and unflooded
(`diamond_mine_flooded_gameplay`, `diamond_mine_not_flooded_gameplay`), the flood being the
pipeline mission's result (`Objectives/A2LM08_05`). UFLL posters.

**M-S Pipeline** and the **Mertens-Segolo pump house.** Pipeline sections on supports, water pumps, a
water tower, welders (`Duty.Welding`). The UFLL is "using a pipeline to pump fresh water out of Lake
Segolo and sell it out-of-country" (`Objectives/A2LM08_01`); the billboard says "Brings Water to your
home!". 11 UFLL posters at the pipeline; blood and a corpse within 200 m.

**Bowaseko Brewing Co.** Beer stacked everywhere (69 beer barrels, 47 beer crates), four stills
(`alambic01`), and a manager's office with masks on the wall, picture frames, bookshelves and
colonial chairs. In its `a2sm08_brewery_trashed` state, seven `Corpses.Corpse`, 15 blood decals and
five large fires. Mbantuwe's hideout (`Objectives/A3SM11_APR_02`). UFLL posters.

**Weelegol Village** (`dusttown`). A market village on the water: 46 awning pieces, 20 livestock
cages, pirogues, docks, 36 wildebeest and gazelle bones, two `Weapons.IED_new`. A
`PTrigger_DJ_Playbark_WaitA` plays the radio DJ. 14 UFLL posters and graffiti.

**Prison.** Cells (`prison_cell_02a` x9, `prison_cage_small` x8, 11 prison doors), 39 drag trails of
blood (`bloodbigtrail_01`, `Prison.Prison_Blood_Trail`) between them, three corpses, flies with
their own sound (`SoundPoint_Prisonflies`), and 21 metal bowls and pans for the prisoners' food. 20
`Rest.SitAgainstWall` points. The player wakes here in `Objectives/A2SM09_APR_01`.

**Marina** (`mikesplace2`). The south's Mike's Bar, built from the same set: 31 weapon hooks, ammo
boxes for every class, `Primary.Fake_AK47`, `Fake_Ithaca`, machetes. The wrecked barge
`MissionObjectives.HornetsNestBarge` lies in its inlet (`a2sm07_wreckedbarge`).

## The south-east: the jungle

**Swamp.** A camp of soldiers searching for diamonds (`Duty.DiamondSearch` x6,
`Social.CollaborativeSurveilance`), axes and a jungle campfire.

**Heart of Darkness** (`mouthofdarkness`). A small jungle camp, 601 entities: huts, campfires, a
bedroll, three corpses, a conversation point (`MouthOfDarkness_MainConvoSTP`). The label names it
after Conrad's novel.

**Landing Zone.** Huts, a radio tower, helicopter parts, and the arms dealer dead in his blood
(`Corpses.ArmsMerchantCorpse`, `PassportBriefcase`): "the diamonds stolen by the Arms Vendor"
(`Objectives/A3SM15_01`).

### The warlords camp

Unlabelled on the map (`warlords`). Eight ceasefire stencils, eight faction logo boards, DZ poles,
two rolled maps (`rollmapworld02`), eight `Duty.CheckMap` points and weapon crates, with
`APR.Walton_Purefoy.Armed` and `UFLL.Anto_Kankaras.Armed` among its occupants. Its layers name six
commanders, `a3sm15_warlord_purefoy`, `_kankaras`, `_voorhees`, `_carbonell`, `_greaves` and
`_quiepo`: "the jungle bivouac" where both factions' commanders meet (`Objectives/A3SM15_01`).
*Implied:* the factions' secret alliance, unmarked on the map because it is meant to be secret, and
dressed with the same ceasefire stencils as the cities.

## Beyond the named places

Most of Bowa-Seko's corpses lie outside any named place. They come in small scenes:

| Where | What is placed | Reading |
| --- | --- | --- |
| 300 m south of Weelegol, (1,338, 1,040) | ten `Corpses.Corpse`, 24 blood decals, a burned bus, a wrecked car, a Datsun, `Carver's tape 7` | *implied*, passengers taken off a bus and shot |
| a campfire in the hills, (1,380, 2,010) | three corpses, 17 blood decals, campfires, gazelle bones, a `DiamondBriefcase_LVL3` | a camp overrun |
| a shack, (1,882, 1,149) | one corpse, eleven blood pools, meat, flies, roaches, gazelle bones, a briefcase | a death left to rot |
| a roadside, (1,060, 1,190) | a corpse with blood, a wrecked car, UFLL graffiti, a Mertens-Segolo billboard, a briefcase | |
| (2,966, 2,116) | `PartnerMissions.PredecessorCorpse` and his dictaphone, in the layer `a3vm09\vmend` | the predecessor of `5124224`, found |

And two cross fields, both described in the [catalogue](./catalogue.md#burial): 41 wooden crosses
alone in a field west of the Ranger Station, and 15 crosses in two rows by a hut camp south-west of
the Marina.

*Implied:* Leboa-Sako's dead were hauled away by truck. Bowa-Seko's lie where they were shot, or under
fresh crosses with no stones yet.
