---
sidebar_position: 2
---

# The September 2008 prototype against retail

:::info[Verified via reverse engineering]
Prototype data read from the Xbox 360 build of 8 September 2008 and compared with retail PC (1.03
patch over `common.dat`). Text configs diffed attribute by attribute; every entity library
(`entitylibrary_full.fcb` and the per-world ones), `world1.managers.fcb`, the particle libraries and
the sound banks decoded with `jackall-cli`; the prototype's HLSL sources and retail's compiled shader
permutation lists compared option by option. Code claims were traced in `FarCry2_server` and checked
against Steam and GOG `Dunia.dll`. Claims marked *(inferred)* were not read in code. Nothing here was
seen in game.
:::

The prototype is a Far Cry 2 build for the Xbox 360, `version.ini` `FCX-XenonSub-0007.0`,
changelist 293602, dated 8 September 2008. Retail PC is `FCX-PC-0047.10`, changelist 307249, dated
30 September 2008. Three weeks of changelists separate them, so the question this page answers is
narrow: what did the last three weeks of tuning change, and did any of it remove content a
pre-release description promised ([pre-release promises](./pre-release-claims.md))?

The answer is mostly no. Content was locked before the prototype. Retail adds events, sounds,
archetype fields and quality tiers, and lowers or raises a few tuned values in both directions. The
data layers that carry vegetation, physics, shaders and sound variations are identical or richer in
retail.

## What the prototype is

- It sits in the repository's third-party tools folder as `Far Cry 2 Sep 8 2008 prototype`, with the
  game data extracted with names (`jackall-cli` cannot extract it itself, because the archives are FAT
  version 4; see [archives](../file-formats/archives-fat-dat.md)). The loose text configs, the
  entity libraries, the shaders' HLSL sources and `.meta.xml` option lists, and the sound banks are
  all readable.
- The platform is a console. Where the prototype's value is lower or higher than retail's, platform
  tuning cannot be ruled out without an Xbox retail build to compare against. The affected values are
  marked below.
- The prototype ships 147 HLSL shader sources and their `.meta.xml` option lists, which retail does
  not. They are the readable form of the shaders retail ships only compiled.
- The prototype's `startup/` folder is a byte-identical subset of its `common/`.

Counts for the text files (languages and `ui/` excluded): 345 identical, 901 differing (836 of them
Domino Lua scripts, almost all regenerated graphs with renumbered indices), 245 prototype-only (the
HLSL sources, Xbox UI lists) and 277 retail-only (PC UI lists, the unused `tmpla` and `ige_map`
worlds, mission scripts).

## Engine and render settings

### The High preset

The prototype's PC render profile has the tiers ultrahigh, high, medium, low and legacy. Retail
inserts `veryhigh`, which holds the prototype's `high` values, and makes `high` cheaper. Retail Very
High is therefore the prototype's High, and retail Ultra is the prototype's Ultra (except for
`AlwaysMip0Loading`, below). Only the High preset and a few lower ones are cut
*(data census, retail 1.03)*:

| Key | Prototype | Retail |
|---|---|---|
| `RenderQuality/quality[high]@GeometryQuality` | high | medium |
| `RenderQuality/quality[high]@ShadowQuality` | high | medium |
| `Geometry/quality[high]@LodScale`, `KillLodScale`, `ClustersLodScale` | 1.0 | 1.1 |
| `Geometry/quality[high]@RealTreeMinSizeShadowScale` | 1.0 | 1.25 |
| `Geometry/quality[high]@ClusterObjectMinSizeShadowScale`, `SceneObjectMinSizeShadowScale` | 1.25 | 1.50 |
| `Vegetation/quality[high]/shader[Mesh_Grass]@add` | `GRASSQUALITYHIGH` | `GRASSQUALITYCONSOLE` |
| `Vegetation/quality[high]/shader[AAALeaf]@remove` | none | `FAKE_SPECULAR` |
| `Water/quality[high]@WaterReflectionQuality` | 7 | 6 |
| `Post/quality[medium]@DepthOfField` | 1 | 0 |
| `Water/quality[low,medium]` shader `remove` | none | adds `HIGHQUALITYNORMALS` |
| `Vegetation/quality[low,medium]` (`AAA`, `RealtreeTrunk`) | has `AMBIENT_SPECULAR` | loses it |

The `highd3d10` preset keeps Geometry and Shadow at `high`. `GRASSQUALITYCONSOLE` is
`GRASSQUALITYHIGH` plus `CHEAPSIM`, a sway offset without length preservation (prototype
`mesh_grass.fx`), so High grass moves like the console's. Every key above is registered by the
renderer's config classes (server `CRenderGeometryConfig::RegisterProperties` `0x0982F3F0`; the
names are present in Dunia).

In the High preset's geometry tier (`medium` in retail), the prototype-to-retail values also include
`LeavesRatio` 1.0 to 0.5, `RealTreeCapsMaxDistance` 90, and `ClusterObjectMinSize` and
`RealTreeLeafMinSize` 0.02 to 0.04. Grass and clusters therefore cull sooner and at a larger screen
size on High; what `LeavesRatio` does to the leaf cards is *(inferred)* from its name.

### Ultra and mip 0

`TextureResolution/quality[ultrahigh]@AlwaysMip0Loading` is 1 in the prototype and 0 in retail,
where every tier is 0. The key keeps full-resolution textures resident; Dunia registers it
(Steam `0x103F7E28`).

### Threads

`defaultthreadingconfig.xml` in the prototype and in retail's `common.dat` is the same:
`PHYSIC_THREADS` relative to the core count (cores minus 4, at most 3) and `JOB_THREADS` (cores minus
3, at most 4). The 1.03 `patch.dat` sets `PHYSIC_THREADS` to none and `JOB_THREADS` to a maximum of
1. This changes throughput, not the physics limits, which are unchanged. The consumer of the keys was
not traced.

### Soft particles

In `world1` and `world2`'s particle libraries, 164 and 176 emitters (sandstorm, explosions, fires,
vehicle smoke) have `Soft` 1 in the prototype. In retail they have `Soft` 0 and a new
`SoftHighQuality="1"`, and 62 more carry `SoftHighQuality="0"`. Retail's `Environment/quality` adds
`HighQualityParticles="1"` at Very High and Ultra only. Both keys are registered (Steam `0x103D1587`,
`0x103F66EE`); that the one gates the other is *(inferred)*. No emit distance (`MaxEmitDist`) changed.

### `specialLodKillScale`

`gamemodesconfig.xml` adds `specialLodKillScale="1.2"` to the single-player descriptors `Single` and
`FCXSingle` (absent in the prototype; not on the editor or multiplayer modes). The attribute is
parsed (server `CGameModeDesc::LoadProperties` `0x093F8670`, stored at `+0x24` with a constructor
default of 1.0; Steam `0x105788A0`, `+0x4C`), but no reader of the stored value was found. By the
name and the render config's `KillLodScale` it likely culls objects at about 1/1.2 of their distance
in the campaign only. Turning it off is a data edit.

### Settings that are equal

- `defaultengineconfig.xml`: `WorldConfig` `RingData="4,5,10,20,1"` and `RingLod="2,4"` equal.
  `PhysicConfig` equal at the tier in use (the prototype names its top tier `Ultra`, retail
  `VeryHigh`); retail only lowers `Low`.
- `defaultgameconfig.xml` `FireConfig` equal, apart from the tier name.
- `defaultrealtreeconfig.xml`: retail sets `UltraQuality@Softbody` and `MultiEnable` to 1 where the
  prototype has 0; everything else equal. `BudgetManagementOn` and `FireParticleScale` are in the
  file but have no string in either binary.
- The Xbox 360 and PS3 variants under `engine/settings/xenon` and `ps3` are identical apart from
  three shader-preload keys.
- `world1.game.xml` and `world2.game.xml`: `Environment`, grids, shadow, camera and fake terrain
  identical.
- `config/soundconfig.xml` (`occmul_pc="1.0"` against `occmul_xenon="50.0"` in both builds),
  `decal.xml`, `physicmaterial.xml`, `soundpoint.xml`, `watersoundmanager.xml`,
  `materialimpacts.xml` and `missions.xml` identical.
- The world dependency lists differ by 11 to 13 resources only in the prototype (a checkpoint
  building, a radio light, a bench, a barrel, two rocks, `pkg_grin`, civilian reaction moves) and 43
  to 52 only in retail. No vegetation.

## Game rules and combat values

All from the entity libraries and `gamemodesconfig.xml`; every field is registered in the server and
in Dunia. Where a reader was traced it is named.

### Damage and shooting

| Value | Prototype | Retail |
|---|---|---|
| AI automatic rifles' `archDamageMultiplierNotVisibleCurve` (AK-47, FAL, G3, M16, M249, PKM) | no curve name: multiplier 1 at every range | `AssaultDamageMultiplierNotVisible`: 1.0 at 0 m, 0.8 at 20 m, 0.65 at 50 m, 0.5 at 100 m and beyond |
| AI rifles' `archDamageMultiplierVisibleCurve` | no curve name | `AssaultDamageMultiplierVisible`: 1.0 flat |
| SPAS-12 visible/not-visible curves | none | `ShotgunDamageMultiplier*`: 1.0 to 10 m, 0.9 at 30 m, 0.75 at 50 to 80 m, 0 at 510 m |
| Ithaca curves | none | `IthacaDamageMultiplier*`: 0.9 at 0 to 10 m, falling to 0 at 510 m |
| Dragunov `Stim_ImpactDamage nLevel` | 22 | 25 |
| M1903 `Stim_ImpactDamage nLevel` | 23 | 26 |
| USAS-12 `iBulletsShot` | 8 | 7 |
| M249 and PKM `AIShootingSystem` `fBurstLengthLong_Max` / `Long_Min` / `Mid_Max` | 1.2 / 0.8 / 0.8 | 3 / 1 / 1 |
| Reliability `Failure` jam chance per reload (`fJamProbabilityPerReload`) | AK-47 0.06; G3, M249, USAS, Makarov 0.15; M16 0.18 | all multiplied by 4/3: 0.08, 0.20, 0.24; the rest 0.09 to 0.12 and 0.12 to 0.16 |

The prototype carries its own, steeper copies of the Assault curves (0.5 at 510 m visible, 0.3 not
visible), but no weapon names them. With no curve name the damage multiplier is 1
(`GetAIDamageMultiplier`, see [stims and damage](../engine-internals/stims-and-damage.md)). In the
prototype a soldier's automatic-rifle shot at a player he cannot see therefore did full damage; in
retail it does 50 to 80 percent. The Low, Medium and High reliability levels' jam chances are the
same in both builds.

`AIShootingSystem/nForcedFailure{Min,Max}` per difficulty is the number of shots a soldier misses
outright after a hit ([AI](../engine-internals/ai.md)). Prototype to retail, minimum to maximum:

| Weapon | Change |
|---|---|
| M249 | Casual 12 to 15 becomes 4 to 7; Experienced 8 to 10 becomes 3 to 5; Hardcore 4 to 6 becomes 1 to 2 |
| MAC-10 | Casual 12 to 15 becomes 6 to 10; Experienced 6 to 12 becomes 5 to 8; Infamous 3 to 4 becomes 0 |
| Uzi | Casual 12 becomes 6 to 10; Infamous 2 becomes 0 |
| M16 | Casual minimum 6 becomes 1; Experienced 3 becomes 1 to 2 |
| Ithaca | about halved on every difficulty; Infamous 1 becomes 0 |
| Dragunov, M1903 | 3 to 4 becomes 2; Hardcore 1 to 2 becomes 0 to 1 |
| SPAS-12 | minimum lower by 1 |

FAL, PKM and the Uzi's Experienced and Hardcore rows go the other way. Retail soldiers string hits
together sooner, most of all with the M249 and the SMGs.

Ithaca and SPAS-12 also gain `archPrimary/SecondaryBulletChanceOfSuccessCurve`; the primary one,
`ShotgunPrimaryBulletHitChance`, is flat 1.0 in retail. The prototype holds a copy that falls to 0 at
80 m but names it on no weapon. The engine default used with no curve was not traced, so which side
hits more is not known.

### Player, buddies and launchers

| Value | Prototype | Retail |
|---|---|---|
| Player `HealthMax`, Casual (sickness 0 / 5) | 1425 / 925 | 1800 / 1300 |
| Player `HealthMax`, Experienced | 1200 / 825 | 1400 / 1025 |
| Player `HealthMax`, Hardcore | 1000 / 750 | 1075 / 825 |
| Player `HealthMax`, Infamous | 825 / 700 | 825 / 700 |
| `HealthBarSize`, Casual / Experienced / Hardcore | 285 / 240 / 200 | 360 / 280 / 215 |
| Buddy `fAgentHealth` (all 12 `Buddies.*`) | 900 | 600 |
| M79 `iMaxAmmo` Casual / Experienced / Hardcore / Infamous | 5 / 3 / 3 / 2 | 4 / 2 / 2 / 1 |
| Carl Gustaf and Mortar `iMaxAmmo` | Casual same; 3 / 3 / 2 | Casual same; 2 / 2 / 1 |
| M79 `iClipsForSelfDestruct` | 35 | 20 |
| `Pack/MaxAmmo[syringe]` by difficulty | 4 / 4 / 3 / 2 | 6 / 5 / 4 / 3 |

The prototype's health values are hand-dragged curve knots (1425.23, 924.976); retail's are rounded.
Retail raises the player's maximum health by 25 percent on Casual, 17 percent on Experienced and 7
percent on Hardcore, so the prototype killed the player faster. The prototype's value may be platform
tuning *(console caveat)*. Weapon wear per shot is `MaxReliability` divided by clips divided by
magazine ([cheat flags and economy](../engine-internals/cheat-flags-and-economy.md)), so the
prototype's M79 lasted 75 percent longer. Buddies took 50 percent more punishment in the prototype.

### Downed instead of dead

`gamemodesconfig.xml` `Infamy/Levels` sets the chance a lethal hit downs a soldier instead of killing
him (`failureChance`) and how many soldiers may be down at once (`maxNPCInFailure`); read by
`CanHealthFailureOccur` (server `0x087EE270`, see [stims and damage](../engine-internals/stims-and-damage.md)):

| Reputation | `failureChance` prototype to retail | `maxNPCInFailure` prototype to retail |
|---|---|---|
| 0 to 15 | 10 to 15 | 1 to 2 |
| 16 to 35 | 20 to 25 | 2 to 3 |
| 36 to 65 | 30 to 35 | 3 to 5 |
| 66 to 95 | 40 to 50 | 5 to 8 |
| 96 to 130 | 60 to 65 | 8 to 11 |
| 131 and above | 80 to 80 | 13 to 14 |

The prototype killed outright more often and capped downed soldiers lower.

### Grass and AI sight

`logicmaterials.xml` `Base.Grass` and `Base.Grass_high` `fTransparency` is 0.9 in the prototype and 0
in retail. The value is the transparency of the AI's sight and shot rays
(`CSensorySystemHelpers::ValidateLineOfSightWithTransparency`), so grass-material collision let 90
percent of a sight ray through in the prototype and blocks it in retail. Which surfaces carry these
materials (terrain ground or clumps with collision) was not checked. `Bush` and `Vegetation` are 0.5
in both.

### Adaptive behaviour

The prototype's `AdaptativeBehavior` table has five placeholder columns (`level1` to `level5`, rows
such as Grenade 10 / 20 / 30 / 40 / 50). Retail's has `level0` to `level27`, keyed to the 0 to 27
progression level ([AI](../engine-internals/ai.md)). The two are not comparable; the prototype's
numbers are ramps, not a tuned design.

### Smaller changes

- `BonusService` `health_basic` and `health_advanced` `maxammo` at difficulty 0: 2 to 1.
- Repair manuals for the Carl Gustaf, flare gun, M79, mortar and RPG-7 lose their `unjamtime`.
- Enemy weapon packs: level 27 special Carl Gustaf becomes RPG-7; M16 0.25 to 0.50, M249 0.50 to 0.25,
  SPAS-12 1 to 0.75; Ithaca added at 27; PKM dropped at 14.
- `CExplosive/bShotJustMissedIsUsed` False to True on the M79, MGL-140, RPG-7 and Carl Gustaf, so
  retail raises the AI's "shot just missed" reaction on near-miss explosions.
- `bEmitLight` added to the weapons (True except the MP5, 6P9, Dart Rifle and multiplayer-only guns).
- Explosive props: `ExplosiveBarrel_NEW`, `BarrelDirectional01` and `GasCan01_NEW` gain an `OnEvent`
  explosion (Crush 29, radius 7 to 10, `fPhysImpulse` 200, plus Bang and Burn); the prototype's
  barrels exploded only from damage. `LiquidPropaneTank` `fHealth` 1000 to 700, its small variant 750
  to 700, `TrainFuelCar_BK` state 1 300 to 50.
- Every land vehicle and boat gains `CVehicle/EngineFire/EngineFireStim` (Burn 10, radius 1.25), and
  cars get a `mixInVehicleSoundPreset`.
- No vehicle physics value differs: `CVehicleWheeledPhysComponent`, `CVehicleFloatingPhysComponent`
  and the engine and gearbox fields are identical (see [vehicle physics](../engine-internals/vehicle-physics.md)).
  Retail only adds `DlcSwitch*` offsets and the boats' `BoatParams/bForcePPU`.
- No change in any `CFCXAIComponent`, `CPawn`, `Animals.*`, ragdoll or animation field. The exceptions
  are `Border_Commandant` `selSpecialCharacterType` 40 to 51 and the Red heavies'
  `CGraphicKitComponent/bRadomize` False to True.
- `CPersistComponent/selLevel` 2 (Full) to 1 (Limited) on the 16 `weapons.MountedWeapons.*`, every
  `OA_Ladder.*`, `FireStickyStream` and `MolotovSticky`; retail adds a persist component to every
  `STP_archetypes.*` and `smartterrains.*`.
- `CBuildingInfoComponent/SoundParams/fOcclusionFilter` is 0.01 to 0.03 in the prototype and 0 in
  retail on 60 `VirtualBuilding_*` archetypes. A placed building's own value wins over the
  archetype's, and retail's sectors carry their own values, so the change likely never reaches play
  *(inferred)*.
- Retail's `burnableobjects.xml` has 1,190 entries against 1,191: `fishingbuildingstorywindow_01.xbg`
  is dropped and `chariotbox01` and `chariotbox03` get a new ignitor.
- Retail `DataBaseItemManager` templates 21 and 56: the night environment's fog end (`curveEnd`) 200
  to about 410 to 500.

### Physics, which did not change

Impulses, impulse scales, piercing resistances, `PhysicConfig`, push impulses, hurt lean, the
`fPartsSpeed` census (0 on 303 of 339 explosion blocks, so breakable pieces drop in place) and the
bullet impulse on props up to 200 kg (`fPhysImpulse` scaled by a per-archetype factor) are the same in
both builds' data. The prototype's code constants were not checked, because no prototype binary is
available. See [physics and explosions](./physics-and-explosions.md).

## Vegetation

Retail did not thin the vegetation in data. The counts come from the `posZList` of every
`CCollectionComponent` zone in every landmark file of both worlds:

| | World 1 prototype | World 1 retail | World 2 prototype | World 2 retail |
|---|---|---|---|---|
| Landmark files (far / near) | 3,147 / 2,230 | 3,147 / 2,230 | 3,263 / 2,041 | 3,263 / 2,040 |
| Zones | 5,357 | 5,357 | 5,273 | 5,273 |
| Clusters | 174,595 | 174,598 | 181,268 | 181,249 |
| Instances | 2,472,754 | 2,472,738 | 2,128,095 | 2,128,076 |
| Grass `.xbg` | 2,341,897 | 2,341,882 | 1,958,034 | 1,958,097 |
| RealTree `.rtx` | 101,602 | 101,601 | 112,002 | 111,922 |
| Rocks | 9,166 | 9,166 | 10,342 | 10,339 |
| Other `.xbg` (impostor bushes, dung) | 19,577 | 19,577 | 46,693 | 46,694 |

World 1 has 3,447 of its 5,377 landmark files byte-identical and 21 that differ in count (minus 10 to
plus 2). World 2 has 2,977 identical and 330 that differ (minus 69 to plus 43; 161 lower, 169 higher,
net minus 19). The changes are local repaints, for example `rt_bush_olapa` minus 99 and
`1_grasssavannah_a` minus 221 with `_b` plus 211 in world 2. Of 400 changed files of equal count, 186
differ only in `colorsList` (ground colour, sky re-bake).

- `world1.managers.fcb` `CCollectionManager/ResourcesList` holds the same 187 entries in both builds,
  with the same `classId`, `flags`, `playerCoverage` and `physPathId`.
- `ZoneLogicManager` `DensityScale` per `Logic.*` zone is identical (DenseJungle and HOD 2,
  MediumJungle 1.5, Woodland 1.15, others 1). Its only consumers are `CZoneLogicManager::GetZone`
  callers in the editor's scatter and `CZoneInfoComponent::Update`, so it likely feeds the editor's
  scatter and changes nothing in baked vegetation *(inferred)*.
- The RealTree archetypes (28 trees, 22 plants, 19 `AutoPrefab.Realtree_*`, `RealTree.Particle.Default`)
  are identical apart from the entity id: a `CRealtreeComponent`, a `CIgnitorComponent`
  (`bIgniteOnBurn` only), persist level 0 and the `RealtreeDefault` stim table. None carries a
  breakable, spawn-on-destroy or particle-on-collision component.
- `realtreefx.xml`, the per-species reaction table, differs only in `BreakingBranch` sounds: the
  prototype plays one placeholder (`0x004493F4`) for every branch size, retail has four sized sounds
  (`0x004493D4` to `D7`) and a mass sound. The radii, particles and vehicle-impact effects are equal.
  `RealtreeFxManager` `hidResourceCount` is 40 in the prototype and 39 in retail; which resource went
  was not resolved.
- The particle library has 365 systems in the prototype and 370 in retail, none removed. 100 changed,
  all in render flags (`Soft`, `SoftHighQuality`, `Masked`, `LightingExponent`); no emitter count,
  lifetime or emission distance fell.
- Runtime thinning exists only in the render presets: the High preset above, and retail's new
  `legacy` tier with `DensityLodScale` 1.05 (LightJungle), 1.2 (Savannah) and 1.1 (Woodland). The
  console command `gfx_DensityLodScale <zone> <scale>` (server `CRenderConfig::ChangeDensityLodScale`
  `0x0982CFE0`) changes it live.

A denser look in the 2007 demos is therefore either earlier than this build or a render-time effect;
this build places the same vegetation. See [vegetation, fire and weather](./vegetation-fire-weather.md).

## World and entity content

The libraries of `world1` and `world2` add no difference that is not already in `_full`. `_full` has
5,543 archetypes in the prototype and 5,566 in retail; `world2_entitylibrary_full` is
byte-identical to `world1`'s in each build.

- **`vehicle.Land.Quad`** (and `.Multi`) is a full but unfinished vehicle in the prototype, with
  `graphics\vehicles\land\quad\quad.xbg` and `.hkx`, seat, kick, run-over and damage-switch data, no
  sounds and no particles. Retail has no such archetype; the quad later shipped as the DLC vehicle
  `vehicle.Land.DLC_Vehicle1_DLC1`.
- **`Animals.Scripted.Gazelle`** and `OA_DetailObjects.BeerKeg_BK` are in the prototype's world 1
  library and not in retail's, though both stay in retail `_full`. The prototype places the scripted
  gazelle in `w1_b_2` `worldsector4025`. If retail still places it there, it does not spawn, because
  an entity with a missing archetype is dropped *(inferred; the retail sector was not read)*.
- Multiplayer: `pickups.Weapons.AK47_new` and the DLC weapon crates differ; not covered here.
- Retail turns off `CAIShootMeObject/bUsedByAI` on `Kiln_ThinPropaneTank_MissionObjective`, and
  `CMapIntelligence/bDisplayOnMap` on `ScriptedBigTruck`.
- `MortarShell` and `MortarTracer` lose their in-flight `sndActive` whistle; retail plays a new falling
  sound from the tracer.
- Domino mission scripts: the four `domino/system` scripts with real edits are `interruptanim`,
  `bargeassault`, `objectivestate` and `buddywager`; retail adds `jacksbuddy`, `setbuddyhealth`,
  `saveafterteleport`, `entityremoved`, the A3SM15 diamond and healing-trigger scripts and
  `popupconvoymission`. The patch adds `buddydown.gosm.xml` and `revive.gosm.xml`. Retail's state
  machines add `FallToDeath`, the vehicle `bailout`, malaria input locks and an HMR
  `InjuredAllowShoot` group; nothing is removed that changes play.

## Sound

The prototype has no more sound content than retail (sound banks decoded for both builds). The Xbox
audio is XMA1 in big-endian records with a 124-byte resource sub-header, against retail's 128; the
banks for weapons, impacts, vehicles and ambience sit in `world1.dat` (3,745 banks) and `world2.dat`
(3,650).

### Sample rates

Retail took 471 samples that the prototype carries at 48 kHz down to 32 kHz, which removes everything
above 16 kHz (sample descriptor word `[19]`; 305 banks, 728 s of audio):

| Group | Samples |
|---|---|
| Bullet impacts (101 of 137; material switch `0x004565A4`) | 101 |
| Melee strikes (of 49) | 33 |
| Gun unjam clatters | 43 |
| Reload, jam, draw and holster clips, all weapons | about 250 |
| Bullet fly-bys (11 of 19) | 11 |
| Dropped-weapon impacts | a few |

In the 48 kHz prototype versions, 312 of the 471 put more than 0.1 percent of their energy above 16
kHz and 147 more than 1 percent (the top cases, at 5 to 11 percent, are M-16, MAC-10, Makarov,
Dragunov and Uzi reload clicks); in every retail version the edge is 16.0 kHz. The fly-bys are
already band-limited at 12 kHz in the prototype and lose nothing. A further 224 samples went from 34
to 32 kHz and 10 from 44.1 to 32 kHz without loss, because the prototype's audio there ends near
13.5 kHz. Gunshots (70 at 48 kHz, 74 at 44.1 kHz), explosions, ambience, music and vehicles kept
their rates. 343 weapon, impact and explosion samples are 48 kHz in both builds, and there retail's
ADPCM or Vorbis keeps at least as much high band (1.18 percent above 16 kHz against 0.79 percent), so
the loss is the sample rate, not the codec. Localised world banks: all 2,158 prototype samples are in
retail, at equal or higher rate (22.05 to 32 kHz for 1,038), with no length change.

### Content

- Across the 3,937 shared resources and 3,928 shared events, every switch, random container,
  multilayer and multi-event has the same children in both builds: no variation or layer was cut.
- Retail adds 454 events, 45 resources, 37 samples (151 s) and 423 dialogue banks: hit-player and
  headshot sounds (`004C65F3`, `004C6606`), a low-quality shiny reveal (`004F01B6`), the diamond acquired tingling (`004F0E85`), the
  falling-mortar whistle (`004F01B8`), a neon light and wall collapses.
- The prototype has three banks retail lacks: `004493F4` (the placeholder branch break above),
  `004E1D91` (an 8.7 s mono scripted effect only the Hornet's Nest barge sequence played; what it is
  was not identified) and `804C64D6` (a wheels-over-debris container, replaced by retail event
  `004F2207` playing `004F2210`).
- `sndSingleBulletShotEcho`, `sndPassByWizz` and `sndmlDistanceFromShootingSoundToPlayerMultilayer`
  are `0xFFFFFFFF` on all 91 weapons in the prototype as well.
- Retail adds mission-item pickup sounds (19 archetypes), buddy-rescue start and stop sounds on the
  player, the in-vehicle mix on nine vehicles, and sets the player kits' post-FX 3D sound type to 5
  (from -1). Streamed audio is the same 174 ids, XMA in the prototype and Ogg Vorbis in retail;
  `sound_english` holds the same 4,075 ids.

### Reverb

The prototype has no environmental reverb. `7fffffff.bao` holds 63 presets with the same ids in both
builds; the prototype's are 52-byte I3DL2-style records, retail's 112-byte EAX 4 records. Presets 30
to 62, which all 17 set-reverb events select (the savannah, jungle, desert and lake level reverbs,
buildings, no reverb), are Room, Reflections and Reverb at -10000 (silent) in the prototype, and hold
authored EAX values in retail. Retail's descriptor also raises the simultaneous-stream cap
(`+0x8C`, 32 to 64) and sets the EAX obstruction low-frequency ratio (`+0x90`, 0 to 0.3).
See [audio runtime](../engine-internals/audio-runtime.md).

### Mix and levels

- `soundmixings.xml` `Compatible.In_Vehicles`: the prototype mutes ambience types 0, 1 and 24 by 96 dB
  and dialogue, barks, weapons and the rest by 20 dB, but no vehicle references the preset
  (`mixInVehicleSoundPreset` is `FFFFFFFF` everywhere), so it never applied. Retail wires it to the
  Datsun, Jeep Liberty and BigTruck families with -12 dB on ambience only and 0.2 s fades.
- `Exclusive.Buddy_Rescue`: the prototype mutes music types 16 to 18 and plays `0x004F01AA` and `AB`
  from the preset; retail moves the two sounds to the player's `sndBuddyRescueSoundStart` and
  `Stop` and drops the mute. `Exclusive.Phone_Call` `bSavable` 1 to 0.
- 52 sample gains differ. Quieter in retail: ear ringing (-4 to -18 dB), melee swish (-18 to -24),
  Makarov single shot (0 to -4), malaria start (-5 to -9), buddy-rescue gasp (-3 to -8). Louder:
  exploration music starts (-14 to 0) and the M-16 reload (-12 to 0).
- Rolloff curves: 13 of 95 changed. Retail lengthens vehicle engines (`004406CD`, 103 events, 82 to
  120 m), foliage impacts (29.5 to 60 m), the flamethrower, Carl Gustaf and similar (152 to 200 m),
  and lowers the main dialogue curve at 14 m (-12.6 to -14.8 dB).
- Sound regions: every `SoundRegion` is equal. The day and night ambience timeline has 8 entries in the
  prototype and 7 in retail, with transitions at 1, 7, 15 and 20 h instead of 2, 8, 17, 18 and 21 h.
  `Inibition` for animal sounds (type 21, 12 s) reaches 200 m in retail against 100 m; the server's
  `CAmbianceManager::SetInhibition` (`0x09645E00`) is called from `ShootBullet`, `FireBullet` and
  `StartExplosion`, and presumably silences the sounds for the duration *(inferred)*.
- Tails: the AK-family autofire stops are 1.64 to 1.76 s in the prototype and 1.39 to 1.59 s in
  retail, the difference being silence. The six object water splashes run 2.0 to 3.2 s against 1.4 to
  2.5 s, with a correlation near 0, so they are re-cut or different takes.
- `DARE.INI` `[Renderer options] Output frequency` is absent from retail's INI; without it the
  renderer builds its output format at 44,100 Hz (`FUN_10a50f50`). Where that matters, given the
  48 kHz voice-pool buffers, was not traced.

## Shaders

Nothing was dropped from the shader option set. Per-shader option names are identical in the
prototype's HLSL meta files and retail's `fastinitdata` permutation lists (86 shaders in each; retail
147,828 D3D9 and 93,605 D3D10 permutations). Retail only adds: `AMBIENT_SPECULAR` on `aaa` and
`realtreetrunk`, `HIGHQUALITYNORMALS` on water, `CLOUD_QUALITY_ULTRAHIGH`, alpha-to-coverage and the
adaptive-bloom `QUERY` and `RESOLVE_SMOOTH`. The shader meta also drops `FIRST_PERSON` and adds
`TBUFFER_INSTANCING` and `ALPHA_TO_COVERAGE`.

The options that never compile on PC are platform-only by their meta (`SPU_GRASS`,
`SPU_GRASS_TEXTURE`, `NEAR_GRASS`, `MERGE_NOISE` and `POW_LUT` are PS3, `TBUFFER_INSTANCING` is D3D10)
or have no code behind them. The Xbox resolve merges compile to nothing on PC by design. The one
effect retail's final pass lost is the "Sin City" keep-red post effect, whose compiled variants map to
the same objects as their plain twins ([rendering](./rendering.md)). The prototype's
`Platform="d3d9"` render profile has the same top tier as retail Ultra except `AlwaysMip0Loading`.
Where the Xbox variant stands above retail PC: water reflection `ReflectionMultiSampleMode` 4 against
0 at Very High, and `AlphaToCoverage` 1 against 0 at the root.

## Uncertain

- The consumer of `specialLodKillScale` was not found; the culling effect is inferred from the name.
- Whether `LeavesRatio` halves the leaf cards, and how `SoftHighQuality` and `HighQualityParticles`
  gate each other, were not traced in the renderer.
- Platform tuning could explain the lower player health, the building occlusion values and the
  AI's forced-miss counts; no Xbox retail build was compared.
- Whether the retail sector placing the scripted gazelle drops it silently was not checked.
- The AI shotgun hit chance with no curve (the engine default) was not traced.
- The prototype's code, as opposed to its data, was not available; every code constant compared above
  is retail's.
