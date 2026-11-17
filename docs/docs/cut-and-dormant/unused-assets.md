---
sidebar_position: 9
---

# Unused assets — what ships and nothing reaches

:::info[Verified via reverse engineering]
A census of single-player data against every way the game can name something. Archetypes: the
`entitylibrary.fcb` of world 1, world 2 and DLC1 (2,534 archetypes) and world 1's
`entitylibrary_full.fcb` (5,566 names), with every `worldsector*.data.fcb` of both worlds (4,786
files), the managers, Domino Lua, brains, configs and the string tables of the Steam **`Dunia.dll`**
and `FarCry2_server` (the symbolized Linux dedicated server), all decoded with `jackall-cli`. Whole
orphan files are on [asset reachability](../engine-internals/asset-reachability.md); this page covers
entries *inside* files that are read. Animations come from `movemgr.bin` and every shipped `.mab`,
sounds from every `.spk` bank, bark `.bank` and `.sbao`, UI from every decoded `.mgb` package.
Nothing on this page was seen in game.
:::

A thing is "unused" here when nothing in the single-player game names it: no sector places it, no
script spawns it, no state machine or brain asks for it, no code string or hash refers to it. Some
of it is finished art that a data edit would bring in; some is a name over an empty shell. Each
entry says which. Use "ships" for art that is in the retail archives.

How an archetype is named matters for the census. A string reference is found by name, a hash
reference by its CRC32, and some references are composed at run time: a weapon finds its properties
by replacing the first segment of its name with `WeaponProperties` (`weapons.Primary.AK47` becomes
`WeaponProperties.Primary.AK47`, `CPickupWeapon::UpdateWeaponProperties`, Steam `0x100999f0`), and
`CBuddiesManager::SpawnBuddy` composes `<buddy>_Betrayed`. A name that only a composed reference
reaches counts as used.

## Bringing an archetype in

Single-player reads `worlds\world1|world2\generated\entitylibrary.fcb` (1,419 and 1,469
archetypes) plus the DLC1 library (42 archetypes). `entitylibrary_full.fcb` is not read in
single-player ([entity instancing](../engine-internals/entity-instancing.md)). An archetype that
exists only there, marked **`_full`** below, is absent from the running game: a placement or spawn of
its name fails silently. Using one takes adding the archetype to a library the game reads, or to an
`EntityLibraryPatchOverride.fcb`, and then placing or spawning it. An archetype that is in a library
but referenced by nothing needs only the placement.

Of the 2,534 archetypes in the single-player libraries, 1,885 are reached, the multiplayer ones are
tagged `.Multi`, and 178 non-multiplayer archetypes are referenced by nothing. The ones that matter
are below.

## Vehicles

| Archetype | State | What it is | Ships | Using it |
| --- | --- | --- | --- | --- |
| `vehicle.Sea.FishingBoat.M2_Mounted`, `.MK19_Mounted` | `_full` | fishing boats with a mounted M2 or MK19 at `gunner_entry06` (`CVehicle/archMountedWeapon` is `weapons.MountedWeapons.M2_FishingBoat` or `MK19_FishingBoat`) | MK19 turret mesh and skeleton (`graphics\weapons\special\mk19_turret\turret_mk19.xbg`) | add both boats; put them on a river patrol. The two mounted-weapon archetypes are in both libraries and named by nothing, because the boats are absent |
| `vehicle.Sea.FishingBoat.M249_Mounted` with `GhostPatrols.Patrols.FishingBoat.M249_Mounted` | world 2 library | an armed fishing-boat river patrol that no sector or spawner names | yes | place the patrol |
| `vehicle.Sea.GunBoat` | world 2 library, `.Multi` in the patch | a static mesh only (`graphics\vehicles\sea\veh_gunboat\veh_gunboat.xbg`, 117 KB, `CStaticGraphicComponent`, no physics, no `CVehicle`), placed nowhere. `gamemodesconfig.xml` has a `<Plan name="gunboat_vehicle_manual" object="gunboat">`, but no bazaar item sells it | mesh only | set dressing by placement; a drivable gunboat needs a `.hkx` and vehicle data |
| `vehicle.Land.BigTruck.Tanker`, `vehicle.Land.BigTruck_Tanker` | `_full` | a complete drivable BigTruck with doors, hood, windows and gauges, as a fuel tanker; named by no sector, MP map or editor palette | `bigtruck_tanker.xbg` and `.hkx` (the static prop `bigtruck_tanker_static.xbg` is the one the game places) | add the archetype; place it or give it to a convoy |
| `vehicle.Air.Paraglider.Paraglider_Lv1`, `_Lv2`, `_Lv4` | `_full` | glider variants. Single-player has the base, `_Lv3` and `_Lv5`, which implies a five-step progression with two steps shipped | not checked | add the archetypes |
| `vehicle.Land.Rover.StuckRover`, `BrokenRover` | `_full` | set-dressing or ambush variants of the Rover | not checked | add and place |

Two vehicle flags are dormant in data rather than missing: `bUseTimedSteering` is True on all 73
single-player wheeled vehicles, so the direct (non-timed) steering model in
`CPhysWheeledVehicleEntityCreateParams` is never selected, and `bImpulseOnDetach` is False on every
boat (35) and paraglider (9) part. See [physics and explosions](./physics-and-explosions.md) for
the dormant data switches.

## Characters and soldier classes

| Archetype | State | What it is | Using it |
| --- | --- | --- | --- |
| `enemy_archetypes.Red_Faction.CarlGustaf_Caucasian`, `_Nubian`; `Red_Faction.MortarMan_Caucasian`, `_Nubian`; `Red_Faction.Sniper_Caucasian`; `Blue_Faction.CarlGustaf_Caucasian` | `_full` | heavy-weapon soldier classes. Single-player sectors place `Blue_Faction.MortarMan_Nubian` 3 times, `MortarMan_Caucasian` once and `CarlGustaf_Nubian` once (all in world 2). The faction is assigned at run time, so the name's colour is not the faction; the real gap is that mortar men and Carl Gustaf soldiers almost never appear | add the archetypes and place them, or change a post's classes. The class list is in [enemies](../modding/guide/enemies.md) |
| `enemy_archetypes.Red_Faction.RocketMan_Nubian` | world 1 library | a red RPG soldier no spawner or sector names. Listed in the enemies guide, not marked unused there | place it |
| `MercTest.*` (10), `Missions.MissionBriefingTest` | `_full` | developer test soldiers | none |
| `buddies.MissionSpecific.PoliceOfficer` | `_full` | a policeman; the mesh `graphics\characters\storyspecific\policeman\policeman.xbg` and the facial `policeman.lfa` ship | add and place |
| `buddies.MissionSpecific.Prison_Physician`, `SoF_Generic` | `_full` | mission-specific characters | add and place |
| `buddies.Buddies.Wound` | `_full` | a buddy on `graphics\characters\avatar\avatar_wound_test.xbg` (ships), a wound-test body | none |
| `buddies.Buddies.Biped` | `_full` | a generic buddy on Xianyong Bai's mesh | add and place |
| `Ubidays_Specific.World2_SafeHouse_Ubidays.Sofa_Ubidays` | `_full` | a leftover of the Ubidays demo mission, which [cut content](../lore/missions/cut-content.md) covers | none |

The twelve `buddies.Buddies.<Name>.<Name>_Betrayed` archetypes are **used**. `SpawnBuddy`
(`0x0875b9a0` in the server, string `_Betrayed` at server `0a11b6c4`, Steam `10e9ed6c`) builds the
name `<ArchEntity>.<BuddyName>_Betrayed` when the current mission id is `A3SM15`, the finale, and
spawns the armed enemy version. They ship in both libraries (24 names) and none in DLC1. They carry
enemy-grade tuning in place of a buddy's (combat multipliers 0.75 / 1 / 1.25 where a buddy has
4 / 4 / 4, point-blank timer 1.5 s against 0.5 s). Outside `A3SM15` they are never built, so a
betrayed buddy earlier in the game takes a mission-id check change or a Lua spawn by name; the
buddies guide ([buddies](../modding/guide/buddies.md)) already says so.

## Weapons, gadgets and pickups

| Archetype | State | What it is | Using it |
| --- | --- | --- | --- |
| `weapons.Grenades.UnstableM67` | `_full` | an M67 with `CExplosive` and `CParticlePhysComponent`; the name suggests a cooked or faulty grenade, and its tuning was not read | add |
| `gadgets.Equipped.Binoculars` | `_full` | gadget `sName=binoculars` on the monocular mesh, with `TransitionCurve=weapons.AimCurves.IronsightFOVCurve` | add; tuning not read |
| `pickups.WeaponsCrate.*` (26) | `_full` | `CPickupContainer` weapon boxes (`boxweapons_assault.xbg`) that hold one weapon at bone `WeaponPlacement10`, one per weapon plus M67 and Molotov crates. The `use_pickup_containers` setting is 1 in single-player, so a placed crate works | add and place; single-player otherwise uses the armory's `WeaponStorage` |
| `pickups.Weapons.*_new.Rusty` (14), `weapons.*.Mikes_Rusty` (6) | `_full` | rusty weapon pickups and variants | add |
| `weapons.Primary.<Dragunov, FNFAL, MP5, SPAS12>.AI`, `.Persistent`, and the Desert Eagle's | `_full` | AI and persistent variants of weapons | add |
| `weapons.Special.M2`, `weapons.Special.MK19` | `_full` | turret weapons outside a vehicle | add |
| `weapons.HandToHand.Machete_Modern` | in the libraries, referenced by nothing | the `WeaponProperties.HandToHand.Machete_HomeMade`, `_Modern` and `_Primitive` set exists | give it to the player or a pickup |
| `DLC1Weapons.DLC1.Shotgun_Bullet`, `01`, `02`, `03` | DLC1 library | projectiles named by nothing | none |
| `OA_FakeWeapons` (25), `OA_MissionObjectives` `Compressor`, `ElectricalBox01`, `KilnPropaneTank`, `Medicine_ToDestroy` | `_full` | editor and mission-objective props | add and place |
| `OA_MissionObjectives.MissionObjectives.CrumblingRock_BK`, `Pipeline` | world 2 library | `Pipeline_MO` is the one in use | none |

The DLC1 `Pickup_SilencedShotgun` (and `Pickup_Crossbow` and `Pickup_SawedOffShotgun`) are used.
`downloadcontent/dlc1/dominos/domino/system/dlc1weaponsspawn.lua` spawns `DLC1Weapons.DLC1.WeaponCrate`
and those three pickups on ten host entities, five labelled W1B2, W1B3, W1C3, W1C4 and W1D2 and five
W2B2, W2B4, W2C3, W2D2 and W2D4, when the DLC1 layer is mounted. The weapons are not in the bazaar
item lists; the string dumps hold only the HUD icon `hud_silenced_shotgun` and the multiplayer
weapon code `~SS`.

`bCanBeScouted` is False on every pickup class, 265 weapon pickups among them; a reader of the
pickup's own flag was not found, so what True would do is open.

## Animals

*(data census, retail 1.03)*

- **Scripted twins.** `Animals.Scripted.Gazelle`, `Zebra`, `Wildebeest` and `Chicken` are `_full`
  only (`ScriptedAnimal.gosm.xml`, `scriptedanimalbrain.ai.rml`); single-player keeps scripted
  `CapeBuffalo`, `Goat`, `ChickenA` and `ChickenB`.
- **Bats.** `Animals.Winged.Bat` ships mesh, textures and a skeleton, and **no animation clip
  exists** for it. Single birds `Bird01` and `Bird02` are unused; the flock `Animals.Winged.Birds` is
  placed in 116 sectors.
- **Crocodile.** `CAnimal.selAnimalSpecies` has the value 6, `Crocodile`; no MOVE criterion tests
  it, and no crocodile clip, skeleton or model ships (only the `crocohead` and `crocoskin` wall
  decorations).
- **Sound.** Only the gazelle and the zebra have a call: `sndAlertSound 0x004B2872` and
  `sndDeathSound 0x004B2873`, shared. Buffalo, wildebeest, goat, chicken, bats and birds have both
  fields at `0xFFFFFFFF`. There is no predator archetype, and the buffalo's sound notifies cover eat,
  idle, head twitch and run cycle only. All 19 `animal` sound banks are reached.

The species values, the rigs and what an animal can do are on [animals](../engine-internals/animals.md).

## Smart-terrain activities

Smart-terrain points (`STP_archetypes.*`) place a soldier or civilian in an activity: sit, lean,
wipe a bar, stand watch. 24 of them are `_full` only, and the animations that go with them are only
half there. *(verified by looking each clip hash up in the retail `.fat` indexes)*

| Archetype | MOVE state (`mmsAction`) | Animation | Usable now |
| --- | --- | --- | --- |
| `Duty.BodyGuard` | `Pawn_ST_Duty_Guard3` (+ Pre/Post) | 3 clip hashes, none shipped | no |
| `Duty.CleanVerticalSurface` | `Pawn_ST_Duty_CleanVert` | none shipped | no |
| `Duty.WipeBar` | `Pawn_ST_Duty_WipingTable` | 4 hashes, none shipped | no |
| `Duty.LightTorch_PREFAB`, `Duty.ExtinguishTorch_PREFAB` | `pawn_st_duty_kicktire` | 3 kick-tire clips ship | yes, but they play the tyre kick, which looks wrong at a torch |
| `Rest.LeanOnTable`, `Rest.LeanOnTableSocial` | `Pawn_ST_Rest_LeanHoriz3` | none shipped | no |
| `Rest.LeanVerticalLeft/RightShoulder` (+ Social) | `pawn_st_rest_leanvt_left`, `Pawn_ST_Rest_LeanVT` | none shipped | no |
| `Rest.LeanBackToWallSocial` | `Pawn_ST_Rest_LeanBackWall` | ships, used by retail points | yes |
| `Rest.LeanHorizontalHighSocial` | `Pawn_ST_Rest_LeanHoriz1` | ships (world 2 uses it) | yes |
| `Rest.SitOnBenchSocial` | `Pawn_ST_Rest_Sit3` | ships | yes |
| `Rest.SitOnChairSocial` | `Pawn_ST_Rest_SitChairV1` | ships | yes |
| `Rest.SitOnGroundSocial` | `Pawn_ST_Rest_SitOnGround` | ships | yes |
| `Rest.SitOnTableEdgeSocial` | `Pawn_ST_Rest_RestButtTable` | ships | yes |
| `Social.SocialSitAtTable` | `pawn_st_rest_sit2` | ships (`eat2_sitloop`) | yes |
| `Rest.SpectatorA`, `Social.Spectator`, `Social.SpectatorB` | `Pawn_ST_Social_Spectator`, `pawn_st_social_spectatorduo` (synced pair) | none shipped | no |
| `Social.DutyAskDirections`, `Social.RestAskDirections` | `Pawn_ST_Social_AskDirection` (synced) | two named clips (`3rdge_synm/syns_askdirection`), neither shipped | no |
| `Social.KitchenPatrolB` | `pawn_st_duty_potatob` | no such state in the MOVE graph; the `pawn_st_social_potatob` pair has 4 unknown clip hashes, none shipped | no |
| `smartterrains.Generic.*` (8) | none | base types (`Duty`, `Guard`, `Patrol`, `Rest`, `RestAnimal`, `Social`, `SocialDuty`, `SocialRest`) with no content | no |
| `props.Props.ChairSTP` | a chair model with a simple animation | model ships | yes, a chair prop |

Nine of the 24 are placeable as they stand: the seven sit and lean variants whose states retail
points already use, and the two torch points as a kick-tire stand-in. Fifteen need clips that are
not in the shipped data, so the cut work is the animations as well as the points. "Not shipped"
means absent from the retail `.fat` family; none of these names occurs in `downloadcontent`.
The social variants use the same plan workspace (`smartterrain.ai.rml`) as retail points, so the AI
side needs no new task. A new activity takes a `Pawn_ST_*` clip added through a `.mab` layer, then a
placed point.

## Animations and MOVE states

`movemgr.bin` holds 1,687 top-level states. A state can play only when something names it: a gosm
`animStateID`, `layerStateID` or `gestureStateID`, an STP's `mmsAction`, a Domino `PlayAnim` or
`LoadResource`, a bark bank gesture, an in-graph `CMoveStateRef`, or a code string or CRC32. Pre,
Post and Abort states of a smart-terrain action count as named through their base.
*(data census, retail 1.03)*

- **1,382 states are named or derived; 301 are named by nothing**, 300 of them also absent from both
  single-player world deploys. 142 of the 301 are generic (the rest are mission scenes).
- **Clips.** Of 4,270 shipped `.mab` clips, 4,026 are reachable from a named state. 211 belong to the
  DLC1 graph, which `jackall-cli move decode` cannot read (it holds expansions), so they are not
  classified. 4 bird flap clips are played by the flock archetype. **29 clips are played only by
  states nothing names.**
- Most of the 301 are stubs whose clip is `invalidanim.mab` or a hash with no shipped file.

### Finished animation nothing plays

| State | Clips | Why nothing plays them | Using it |
| --- | --- | --- | --- |
| `Pawn_Generic_MountedMovement` | 9 clips in `weapons\mounted\`: aim poses for the Rover's mounted M2, MK19 and M249, first person regular and iron sight (`1stge_fulb_aimreghummount_*`, `1stge_fulb_aimironhummount_*`) and third person (`3rdge_fulb_aimreghummount_*`) | named by no gosm, data or code. The live layer `Pawn_Generic_Mounted` (the `UsingMountedWeapon` layer in `vehicles.gosm.xml`) has only tripod clips, even on its `Vehicle=Rover` third-person branch. The clips are loaded with `turret_m249` and `turret_m2` | point the gunner state at it, or repoint `Pawn_Generic_Mounted`'s Rover branches to the hummount clips (a MOVE fragment, `jackall-cli move fragments`); visual only |
| the `CoverStance` branches | `3rdge_fulb_crouch2blind_+000fw_prak4_i1`, `3rdge_fulb_blindshoot01_..`, `3rdge_fulb_blind2crouch_..` (AK-47, `weapons\primary\ak47\`) | channel 31 `CoverStance` (`None`, `LeaningLeft`, `LeaningRight`, `LeaningUp`, `Blindfiring`) is tested once per value in the main third-person locomotion subtree; the leaning and blind-fire branches all play `invalidanim.mab`, and the three finished clips sit alone in unnamed layered states. The server has the strings `Blindfire` and `EVENT_FS_BLINDFIRE`; whether anything writes `CoverStance` was not traced | fill the Blindfiring branch and have code set `CoverStance`; only the AK-47 has clips |
| `Transition_IdleCombat2Alert` | `3rdge_fulb_idlecombat2alert_+000fw_prak4_i1.mab` | its pair `Transition_IdleAlert2Combat` points to an unshipped clip | a small de-escalation visual |
| `pawn_buddydown_cradleentryshort` | `special_actions\buddy_down\{1stge_syns,3rdge_synm}_cradlesyretteentryshort_..` (a synced pair) | the live buddy-down uses the long entry | a faster start to healing a buddy |
| `Pawn_Generic_Revive_FallDown`, `_Wait` | `hurt\revive\mulge_fulb_fall_..`, `mulge_fulb_sufferloop_..` | multiplayer (`mulge`) | none in single-player |
| smaller | `weapons\secondary\desert_eagle_50\1stge_uppb_recoil2aimironsight_+000fw_sedea_i1.mab` (only in the unnamed `Transition_AutoShoot_End`); mission scene clips `sm02_se01_{apr,ufll}_boss_greet`, `..._exit2loop`, `sm06_se01..greet_idle`, `sm11_se01_{apr,ufll}_greet..`, `sm03_se02a_marty_waita`, `lm01_se03_copkiller_informant_startposition`, `lm04_se03_directspear_informant_startposition` | boss greeting idles and informant start poses used only by unnamed states | mission content; not covered by [cut content](../lore/missions/cut-content.md) |

### Authored states whose clips play elsewhere

These are wiring only: the states are unnamed, but the clips are reachable through another state.

- `pawn_social_aimedat_start`, `_loop`, `_loop2`, `_end`: an alternative "aimed at" reaction, hands
  up high or low by the player's `Infamy`, with or without a gun. The live path (`AimedAtL2`..`L5`
  plans, `social.gosm.xml` `AimedAtLoop` playing `Pawn_Gest_IntimidationStand`) plays the gun-in-hand
  hands-up clips armed and an intimidation stance unarmed; the unnamed set adds the unarmed
  `3rdge_gest_handsup1/2`.
- `pawn_st_duty_First_Aid_{In_Second,Loop,End}`: a duty variant of the medic first-aid sync, with
  clips living on through `pawn_st_social_firstaidb`.
- `pawn_buddydown_ExecuteTellShoot`, `Pawn_Generic_OpenDoor_Locked` and `_Outside` (duplicates of
  `Pawn_Generic_Open_Door`), `pawn_gesture_shove1front` (duplicate of `pawn_gesture_shove`),
  `Pawn_Transition_Jump_Begin/End`, `Pawn_Transition_Normal2Ironsight`, `Pawn_Generic_ChangeAmmo`,
  `Pawn_Machete_killground_begin`, `Pawn_Sync_Machete_FinishGround`,
  `Pawn_Generic_Melee_Charge_Begin_FullBody`, `Transition_Crouch_to_Stand`, `Stand_to_Crouch`.

### Stubs: cut behaviours with no animation

The clip is `invalidanim.mab` or an unshipped hash. Restoring any of these needs new animation.

| Group | States |
| --- | --- |
| Synced machete takedowns | `Pawn_Sync_Machete_BeginKill`, `FinishDefault`, `FinishDisarm`, `FinishDouble`, `FinishFlip`, `FinishGrenade`, `FinishPush`, `FinishSlam`. The server still has `doMacheteFinishDisarm` and `doMacheteFinishGrenade` (not traced); live are `Scare`, `Miss`, the ground finish and the double-kill event |
| AI cover leaning | `LeaningLeft`, `LeaningRight`, `LeaningUp` (above) |
| Gestures never animated | `Pawn_Gesture_PutOutFlames`, `pawn_generic_BurnOnGround`, `Pawn_Gesture_Knock`, `pawn_gesture_shove1left/right`, `Pawn_Gesture_BeckonBig`, `pawn_gesture_QuestionAlert/QuestionRelaxed`, `pawn_gesture_SearchTargetLow`, `pawn_gesture_losttargetb`, `pawn_gesture_covernosea/b`, `pawn_gesture_fingercounting`, `pawn_gesture_middlefingerc`, `pawn_gesture_firingpistol`, `Pawn_Gesture_Flame`, `pawn_gesture_VehicleCollision`, `Pawn_Generic_BulletReaction` |
| Smart-terrain actions with no animation or archetype | `Pawn_ST_Rest_Jumpjack` (exercise), `Pawn_ST_Duty_RefuelVehicle1`, `Pawn_ST_Duty_WipingTable`, `Pawn_ST_Duty_CleanVert`, `Pawn_ST_Duty_Guard3`, `Pawn_ST_Duty_Patrol2`, `Pawn_ST_Duty_AmmoReload1`, `Pawn_ST_Rest_LeanVT`, `pawn_st_rest_leanvt_left`, `Pawn_ST_Rest_LeanHoriz3`, `Pawn_ST_Rest_Eat2All`, `Pawn_ST_Layer_Spitting`, `pawn_st_social_potatob` (a two-man game), `pawn_st_social_sniperspotterb`, `Pawn_ST_Social_Spectator`, `pawn_st_social_spectatorduo`, `Pawn_ST_Social_AskDirection`, `Pawn_ST_Social_AbortedConversation`, the `Pawn_ST_Abort_*` set |
| Player and world interactions | `Pawn_Player_Rescue_EndDrag`, `Pawn_Player_Rescue_Idle` (the player dragging a rescued buddy), `pawn_jackalrescue_entry_anchor`, `Pawn_Generic_GlassCabinet`, `Pawn_Generic_Computer_Cycle`, `Pawn_OpenBriefcase` (uses the diamond pick-up clip), `Pawn_Generic_MapCompass_Targeting`, `Pawn_Multi_Radio_*` (multiplayer) |
| Civilians | `civilians_couple_*_reaction(Pre)`, `civilians_male03_reaction(Pre)`: clips not shipped |
| Swimming | the `Stance` channel's `HeadDown` and `ChestDown` branches (underwater and surface) hold no clips; `Swimming` has one |

### State-machine signals nothing sends

Twenty `.gosm.xml` signals have no sender by text (code strings, brains, input maps, Lua, entity
library) and no CRC32 immediate in either binary:

- the AI weapon reliability failure states in `hmr.gosm.xml` (`ReliabilityFailurePrimary`,
  `ReliabilityFailureSecondary`, `ReliabilityFailureNoWeapon`, `ReliabilityRestore`,
  `ReliaibilityExplose`): the victim draws a sidearm, the gun explodes into a ragdoll, and a
  mechanic merc restores it. They are logic-only states with no MOVE animation besides holster;
- `MoraleExecution`, `MoraleRescue` (a boss executes or rescues a broken merc), `HealthRescue`,
  `RescuerKilled`, `targetdied`;
- `JumpDown` (`ai.gosm.xml`, MOVE `Pawn_Generic_JumpDown`), `start/stop_briefingreaction`,
  `kill_stp`, `kill_stp_layer`, `abort_smart_terrain_layer`, `quickstop_smart_Terrain_layer`,
  `dump_weapon_nodraw`, `force_driving_to_vehicle`, `force_mounted_to_vehicle`.

The gosm reachability pass covers only part of the machines (entry by code-side requests and Domino
`PlayAnim` is not modelled), so the signal list is the usable result and the state list is not.

## Particles, decals and materials

*(data census, retail 1.03)*

- **No single-player particle system is orphaned.** All 397 systems in the world 1 and world 2
  libraries and the 58 in DLC1 are named by an archetype field, a database, a sector entity, a code
  string or an animation event track. Every `text_ps*` value in the libraries and every `ps*` name
  in `common/databases/*.xml` exists, so there is no dangling particle reference. What never plays
  is decided one level up, by the archetype or the clip that would trigger it.
- **Inactive emitters.** 15 emitters in the single-player libraries have `PartEmit Active="0"`, plus
  one in DLC1: `dlc1_particles.Weapons.PL_Muzzleflash_Silentshotgun.smok2`. Setting `Active="1"` in
  the library `.rml` switches each on.
- **One particle hangs on a placed-by-nobody archetype:** `destructibility.objects.potery_explo`
  belongs only to `OA_CoverObjects.CoverObjects.WallBarrier01_BK`.
- **Heal effects.** `healing_effects.heal.fire_on_arm`, `fire_on_l_leg`, `fire_on_r_forearm`,
  `matches_heal`, `blood_ribs`, `large_blood` and `medium_blood` are started only by the event
  tracks of the heal animations, so they play only if the heal state is reached.
- **Storm and tree wind effects.** `real_tree.wind.*` (8 species) are in `realtreefx.xml` and the
  storm branch-tearing code has no caller; see [vegetation, fire and weather](./vegetation-fire-weather.md).

### Material impact records

`common/databases/materialimpacts/materialimpacts.xml` has 64 records, keyed by the CRC32 of the
exact-case `Name`. Five are named by nothing:

| Record | Content | Why it is unused |
| --- | --- | --- |
| `Pawn.Splash` | sound only: `sndSwitchSound 0x004EBE65`, a random container in `004ebe65.spk`, type 14 | no archetype names it; non-player pawns use `Pawn.FootStep` and `Pawn.Land`, the player has `Player.Splash`. Naming it from a pawn's splash impact field would make NPCs splash audibly entering water |
| `Weapon.LargeBullet` | the same particles as `Weapon.Bullet` for 25 materials, a near-identical decal set, no sound (`0xFFFFFFFF`) | every weapon names `Weapon.Bullet` or nothing |
| `Vehicle.WheelTraceHeavy` | the same wheel particles as `WheelTraceLight` | only the light record is named (`matimpWheelDustFx`) |
| `Objects.Boot`, `Objects.Paper`, `Objects.Pestle`, `Objects.Soccerball` | sound only (`0x004E7C38`, `…4A`, `…50`, `…53`) | no archetype names them; the soccer ball uses another record. The bank for `0x004E7C53` does not ship, so that one would play nothing |
| `Weapon.PlasticShell` | empty: no sound, no particles, no decals | nothing names it; shotgun shells use the metal records |

### Decals

`common/databases/generic/decal.xml` has 30 entries; three are named by no impact record, archetype,
stim effect or code string:

| Decal | What it is |
| --- | --- |
| `Base.gre_ground` (`719CE9C7`) | `GFXD_megablast.xbt`, 1 x 1 m, `fLifeTime="0"`, soft. Grenades use `Grenade_imp.Grenade_ground_hard`, the same texture at 12 m and 20 s; naming this one from an impact record gives a smaller, permanent scorch |
| `Base.Plastic` (`63E67B75`) | the small-burn texture; plastic impacts use `Base.Small_burn` |
| `Base.Default` (`602ECAD1`) | a concrete bullet hole, 10 s. A code fallback by hash is possible and was not traced |

Of the 207 `CStaticDecalComponent` placements in 154 single-player sector files, every one has
`logmatLogicMaterial = -1`: static decals never name a logic material.

### Logic materials

`common/databases/materials/logicmaterials.xml` defines ids 0 to 38. Counting `LogicalMaterialId`
over all 2,379 `.xbm` materials (2,350 carry one), the terrain layer table and the Havok shapes of
all 2,745 retail `.hkx` files gives these ids that no mesh material carries:

| Id | Name | Carried by | Effect that never fires |
| --- | --- | --- | --- |
| 28 | `Stubble` | no `.xbm`, no static decal, no `.hkx` | bullet impact `b_imp_straw`, sound switch `4457084` |
| 32 | `Electronic_stuff` | none | spark impacts: `bullet_impact.b_imp_electronic_stuff`, `machete_impact.slash.m_imp_electronic_stuff`, `bounce.bo_imp_electronic_stuff`, sound switch `4494436` |
| 33 | `Metal_burn` | no `.xbm`; **112 Havok shapes in four files**: `carburned01.hkx`, `carburned01_bk.hkx`, `carburned02.hkx` and DLC1's `carburned02.hkx`, the burned car wrecks | reached only through physics, never through a texture; sound switch `4457069` |
| 38 | `Cactus` | none | its impact entries, sound switch `4457073` |

Ids 22 and 23 (water) and 34 (`Grass_high`) are carried by no `.xbm` either, but code uses them
(water hits, `Global.Settings logmatGrassSurfaceID="34"`). Ids 6, 19, 20 and 37 are carried by a
`.xbm` or by terrain. Havok shapes hold small integers in `userData` that match the id table (the
commonest are 16 `Wood_hard`, 11 `Metal_hard`, 2 `Concrete`, 13 `Metal_sheet`). The definitions are
complete, with sound switch values, impact effects and a Havok physic material, so setting
`LogicalMaterialId` on a mesh material (a radio, a generator, a cactus tree) or a `.hkx` shape gives
it distinct impact sounds, particles and decals.

## UI and strings

### Pages

*(data census, retail 1.03)* 591 top-level areas in 24 `pcwidescreen\eng` packages were decoded and
traced from the 195 code-addressable entries of the GenericObjectTable; 580 are reachable. Every
page in the single-player packages is addressed by a name that is a code string or a Domino Lua
string, so no whole screen is cut but left in. The 17 HUD pages whose hashes are not in code are
Lua-addressed: the `MESSAGEBOX_*_TUTORIAL` and `MESSAGEBOX_W1C1` to `W2C4` convoy mission pop-ups,
and `HITINDICATOR_1` and `_2`.

The 11 unreachable areas, none visible in single-player:

| Package | Area | What it is |
| --- | --- | --- |
| common | page `F6DCA705` | one button whose text is base64 for `reubenblog.typepad.com`: a "visit Reuben's blog" page that matches the Coda's line that he will publish the story on his blog. Not in the table, never linked, no hash in `Dunia.dll` |
| common | page `5BEB8187` | a post-it image, a long body text, a title and one button; probably a cut note or handout |
| common | page `50A0D1DB`, area `C23D59DA` | a "Generic;LOADING" text over `loading_logo3`, a variant of the loading overlay |
| common | page `3CB2F678` | an empty text placeholder |
| mp_* | 5 areas | multiplayer |

Showing one takes a code or FCSE hook that pushes the page by its area hash; the blog page also
needs a `CMagmaButton` action. The UI authoring format is in [Magma UI](../magma-ui/index.md); the
menu system's runtime in [the Magma menu system](../engine-internals/magma-menu-system.md).

### The weapon challenge system

The engine has a complete weapon challenge system that nothing starts. *(RE-verified for the code
side; data census for the rest)*

- Code: `CChallengeComponent` (also written to savegames), `CChallengeFactory`, and one class per
  gun (`CChallengeAK47`, `CChallengeRPG7`, `CChallengeUSAS12` and so on). The console commands
  `activate_challenge` and `complete_challenge` exist.
- Data: `gamemodesconfig.xml` has a `<gun>_challenge_bonus` plan per gun (10 percent accuracy and
  recoil).
- Text: the `Challenges.*` string section (27 gun names plus `desert`, `savannah` and `jungle`
  "Camouflage", `UNLOCKED` "A new challenge has been unlocked: CHALLENGE_NAME", `COMPLETED`) and
  `PauseMenu.UPGRADES_CHALLENGES_TITLE`, `UPGRADES_COMPLETE_CHALLENGE`, `UPGRADES_CHALLENGE_COMPLETED`.
- Not present: no Lua node activates or completes a challenge, and no single-player package shows a
  list. Whether the per-gun classes count kills on their own was not traced.

Using it means driving `ActivateChallenge` and `CompleteChallenge` from Lua or a plugin and adding a
page.

### Strings and options with no reader

`oasisstrings.xml` has 5,051 symbolic keys. 1,145 have no reference by name in any `.mgb`, Lua,
config, brain, sector data or either binary. The check is by key name only, and Lua and C++ compose
names (`TU%02d`, a buddy's `<NAME>_AGE`), so an unreferenced key is a candidate. Entries that imply
a cut or dead feature:

- **Options the engine registers and the options page never shows.** `OptionMenu.GAMEOPTION_MINIMAP`
  ("Mini-map"), `GAMEOPTION_ROADSIGNS` ("Highlight Road Signs") and `GAMEOPTION_INVERT_STICKS`.
  `Dunia.dll` holds `UseCompassMiniMap` and `UseRoadSignHilight` as `CGameConfig` members and no code
  reads them ([world loading](../engine-internals/world-loading.md)); a `CRoadSignManager` and
  `CRoadSign` (colour-typed signs) exist in the engine. Turning the highlight on needs a reader hook.
- **Weapon-shop extras with no row.** `WeaponBazaar.WEAPONBAZAAR_IED_*` (an IED crate, operation and
  repair manuals), `MagicCrates.CantUsePrimary/Secondary/Special` ("You need to purchase primary
  crates.") and `Tutorial.TUTORIAL_CRATE_*`. The crate system is real (`CMagicCrate`, three crates
  in the bazaar list; see [cheat flags and economy](../engine-internals/cheat-flags-and-economy.md)),
  but the messages for using a crate you have not bought are never printed. The bazaar item lists
  name only guns, bandoliers, `camo suit`, first-aid manuals and crates, so an IED crate or manual
  was cut.
- **Tutorial entries without a caller.** Lua sets `TutorialType` 1 to 3, 7 to 16 and 18 to 25
  only; types 4, 5, 6 (`TutorialNewWeaponPrimary/Secondary/Special`) and 17 (`TutorialToggleDetailMap`)
  are named in `GetTutorialType` but used by no Lua node. The keys with no reference include `TU21`,
  `TU22`, `TU23` (new primary, secondary, special weapon), `TU18`, `TU19`, `TU01` (camera), `TU04`
  (inventory), `TU10` (pick up), `TU16` (swap weapon), `TU14` (malarial bubble), `TU34` to `TU37`
  (machete, secondary weapons, "open the Upgrades tab", "press to open the map"), `TU44` (molotov
  pickup), `TU57` ("Weapons are sold at the shop in exchange for diamonds"), `TU65`, `TU71`,
  `TU66` and `TU68` (both "[TEXT TO BE REMOVED]"), and `TU99` ("Disable tutorial pop-ups in the
  Options menu"; the Options page has no such toggle). The text id for a tutorial type is chosen in C++
  that was not traced, so these are candidates.
- **Statistics.** `GameStatsMenu.MISSION_GRIN_OPS_CONTACTED` ("Underground operatives met") is never
  listed. `SafeHouse.UGLUnlockedSwoosh` ("Underground cell location unlocked") has no reference at
  all; `GRINUnlockedSwoosh`, which Lua uses, replaced it. `PauseMenu.BUDDY_STATE_UNKNOWN` is unused.
  The counts `Diamonds "...out of 221 briefcases"` and `PGPs "NUM_REPLACE/57 Guard Posts scouted"`
  are in strings that `Dunia.dll` references.
- **Console and mission text.** `Console.*` (32 keys, some multiplayer) is for the console;
  `Objectives.A1LM00_01..03`, `A2LM00_*`, `UbidaysLongDemo_*`, `A1BU00_05/08/09` and `A2BU05_01` are
  cut-mission lines, covered by [cut content](../lore/missions/cut-content.md).
- **Keys that are referenced by suffix or composition and are not unused:** `Items.*`, `Buddies.*`,
  `MessagesBoxContents.QUIT_MESSAGE*`, `Generic.QUICK_*_WIN32`.

Keys that the UI refers to and the string table lacks, so the text is blank (bugs, not cut features):
`sp_pause_menus` uses `REPUTATION_INFAMY_DESCRIPTION` and `REPUTATION_SICKNESS_DESCRIPTION`, so the
journal's two description lines are empty; `common.mgb` uses `GAMEOVER` and
`OPTIONS_DISPLAY_WIDESCREEN`. Every key can be addressed from a mod's own `oasisstrings` layer, so a
cut text can be reused without new text work.

## Sounds and voice

*(data census, retail 1.03; transcriptions are speech recognition of the extracted audio, reliable
for sense and not for names)*

### Banks

Every `.spk` bank and record id was searched in all 57,403 data files and in `Dunia.dll` and
`FarCry2_server`, as a little-endian dword at all alignments and as `0x%08X` text, then followed
through bank records, containers and kind-2 resource tails. Evidence tiers, strongest first: a text
hit or a hit in code, Lua, the entity library or sector data; animation, UI or brain data only; a
bark line whose tag something requests; a bark line of the six never-requested tags (below);
multiplayer files only; nothing.

| Group | Text or code | Animation, UI, brain | Requested bark | Never-requested bark | Multiplayer only | Nothing |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| world banks (4,645) | 3,514 | 62 | 793 | 67 | 184 | 25 |
| common (23) | 22 | | | | | 1 |
| localized dialogue (2,618) | 1,679 | 102 | 617 | 58 | 162 | 0 |

- **Multiplayer announcer.** 183 world banks and 162 localized dialogue banks are the multiplayer
  announcer (capture the flag, VIP, deathmatch lines such as "Enemy Captain is down! Finish him
  off!", archetypes `engine.GameMode.FCXMulti.*`), as are 54 DLC jungle weapon banks and 25 DLC1
  vehicle banks. They are in the shared entity library, which is why a single-player name scan sees
  them as used. A few multiplayer weapon banks too (`004eea5e` crossbow, `004eea68` sawed-off,
  `004eea6d` silenced shotgun).
- **Being outside every depload says nothing.** 741 banks are in no single-player depload and 282 in
  none at all; 529 and 274 of those are reached by data (on-demand loads: vehicle and weapon banks,
  material impact switches, rain and sound regions, UI clicks, object exits). The Land Rover engine
  `0x0044F143` is in no depload and is loaded with its vehicle.
- **The 59 voice clips `404e2f52` to `404e3104` (55 banks) and `404e7bf8`, `404e7c0b`, `404e7c18`,
  `404e7c25` are used.** Their sample ids are named inside other banks, in kind-2 resource tails; they
  are the Jackal tapes and the radio news (668 seconds in all). The tape events are named in
  `gamemodesconfig.xml` (`<Tape ... startSoundID="0x004ebee9">`). The content is interview answers
  and "Dear Jackal" letters, news bulletins ("The United Front and the APR have suspended
  talks...", Ruben Oluwajembi reporting from Port Selao) and Liberation Radio. See
  [the Jackal tapes](../lore/jackal-tapes.md).
- **The 94 records inside loaded banks that no reached record points at are structure, not cut
  variants.** They are children of kind-2 loop, start and stop resources and sample banks that other
  banks point to; following those leaves six, and each of the six is reached from another bank.
- **Banks that nothing names (26, none content).** Eleven are one-record silence stubs (`00448cd7`,
  `00448ceb`, `00448de1`, `00448e92`, `00448e9b`, `00448eb2`, `00449319`, `00449363`, `0044936f`,
  `004569f1`, `00456a01`); most of the rest are stop or exit events of paired start events (stop of
  the LPO50 flame, the G3KA4, USAS12 and MK19 automatic fire; the water pump and pipeline exits
  `00456579`, `0045657b`, `004c64d0`, `004c64d2`, `004c64d5`, `004c6501`; the propane tank init
  `0044f6d9`). The engine probably pairs stop to start by convention, so they are not dead.
  `004f0e82`, 13.7 KB mono, is the diamond acquired tingling in another take (heard); `004f0e88`,
  which `hud.mgb.desc` names, stops `004f0e85` and plays it, so it is reached too. One is a spare
  take: `004c661a`, the `PostFx.Wounds.Down_to_Normal` key, 88 KB stereo, about 2 s, the length of
  the `Down_to_Normal` sequence. `moviedata.xml` plays `0x004C6623` for that key instead, so this is
  probably a superseded take of the revive sound.
- **`.sbao` streams.** How an `.sbao` is referenced (the descriptor's no-inline path, `moviedata.xml`,
  facial `.lfe`) is not fully modelled, so a count of unreferenced streams is not given.

### Bark events nothing requests

A bark plays when code asks for its tag by CRC32, or a script passes the tag as a `PlayBark` `Block`
string. 212 of 223 tags are asked for this way. Of the other 11, five are the phone-call tags
`PH01` to `PH05`, which play anyway because the phone-call scripts give their sound ids directly.
The remaining six never play: no code or script asks for the tag, and every line id resolves to a
real record with audio. The banks load whenever their type loads. What the lines say is on
[AI and combat](./ai-and-combat.md); the bank format on [bark banks](../file-formats/bark-banks.md).

| Tag | Banks | Lines | Content |
| --- | --- | --- | --- |
| `BBX` | 12 buddy banks (type 6), e.g. `272250` | 12, one per buddy | the executed buddy's last words in the finale |
| `MOBILE` | `1201200`, `1244100` (types 28 and 29, the infamy pair of the merc conversation banks); audio in non-localized banks such as `004b01fe.spk`, `004b0202.spk`, `004b0209.spk` | 4 banks, 79 lines | mercs talking on a mobile phone, one side of a call, no subtitles |
| `IIW` | `686400` to `686402`, `729300` to `729302` (types 16 and 17, the infamy pair of crowd chatter) | 6 banks, 30 lines | people shocked at the player's violence toward a third party; no subtitles |
| `IMA` | the same banks | 8 banks, 40 lines | pains and shocks, and reaction to a massacre or a machete rampage; no subtitles |
| `SOL1`, `SOL2` | `1329900`, `1329901`, `1372800`, `1372801` (types 31 and 32, mental states 2 to 4, the infamy pair) | 4 + 4 banks, 23 + 31 lines | soldiers hunting a hidden player (`SOL1`) and taunting him (`SOL2`), conditions `SPS=NA; AIS=AISC; IL=ILL`; no subtitles |

Using one means a brain, behaviour box or Lua `PlayBark` with `Block="MOBILE"` and the like; the
`MOBILE` and `SOL*` pairs also need high or low infamy. `BBX` would need the finale script
(`a3sm15_buddiesbetrayalfinalbattle`) to play the bark when the player shoots the buddy. The tag
CRCs are `2BA074FD` (`BBX`), `CC8BE466` (`MOBILE`), `54BE5F46` (`IIW`), `C4062F13` (`IMA`),
`8E28B59E` (`SOL1`) and `1721E424` (`SOL2`); none is in either binary.

### Mix presets, sound points and music

- **Mix presets that nothing applies.** The CRC of each preset's name appears only in its own file,
  and the name is in no script, no other XML and neither binary. `Exclusive.Death` (absolute,
  priority 2, `sndStart 0x004B645B`, a 3 s stereo music sting labelled "death (player)", which the
  player's own field may still play); `Compatible.Breifings` (music type 18 at -3 dB, the only one
  with content); and empty shells `Exclusive.Burning`, `Exclusive.Flying`, `Exclusive.Normal`,
  `Exclusive.Off-Game Menu` and `Compatible.Dialog`. Burning and Flying are hooks that
  `StartSoundMixingFromLua` or a plugin could fill. The presets that exist but do nothing
  (`Compatible.Ironsight`, `Compatible.Running`, the health, stamina and healing presets) are on
  [audio runtime](../engine-internals/audio-runtime.md).
- **Sound points never placed.** `soundpoint.xml` has 96 points; 68 are placed in single-player
  sectors and 28 are not. Seven are hand-authored ambience: `sfx_fridge_loop_01` (`004C6632`, 25 m),
  `sfx_obj_flame_closeup_2_lp` (`0044F611`), `sfx_obj_outside_woodfire_big` (`0044F70B`) and
  `_small_lp` (`0044F70D`), and `sfx_VillageCrowd_01`, `_02`, `_03` (`004E1F7F` to `81`, 200 m,
  type 11). Using one takes a sound-point entity whose `sndptSoundPoint` is the start id. The other 21
  are `mll_*` fire, vehicle and boat loops (fires on a bridge, mattress, propagation, straw and
  real trees; wheel loops on dirt, grass, mud and wood; `buggy_engine_idle`; boat water contact and
  wind; mortar `adjust_height` and `tracer_lp`). Code probably attaches these, so "never placed"
  need not mean "never played"; only `straw_fire` and `mortar_adjust_height` have a code hit.
- **High-infamy music cue.** `music.xml` `Default` sets `sndHighInfamySoundCue="0xFFFFFFFF"` and
  `sndtpHighInfamySoundCueType="-1"`, and the server registers the field. `soundconfig.xml` defines
  sound type 22 `Infamy_FX`, which no single-player entity field uses; three mix presets only mute it.
  Together they point to a cut high-infamy audio cue, and no orphan bank fills it. Whether
  `CMusicManager` reads the cue was not traced.
- **Animal sound fields** are covered under [animals](#animals) above.

## Uncertain

- The `UnstableM67` and `Binoculars` tuning, and whether the art for the paraglider and rover
  variants ships, were not read.
- The 65 hand props in `props.Props` that no string names (`FishingRod`, `Megaphone`,
  `DigitalCamera`, `Knife_to_eat`, `HealingBranchShrapnel`) are probably bound by MOVE-graph hash,
  which was not checked.
- Which heal and scripted-scene clips (the burn heals, `jakal_jerrican`) actually play depends on
  state reachability that was not fully traced.
- The DLC1 MOVE graph (39 states) was not decoded, so none of its states is classified.
- Whether the mission-scene states (about 158 named `sm*`, `ml*`, `gm*`) are used was not reviewed
  one by one.
- The static-decal and logic-material verdicts rest on the id table matching every sampled Havok
  shape; the lookup was not traced in code.
