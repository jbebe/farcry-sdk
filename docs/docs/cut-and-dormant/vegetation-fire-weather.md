---
sidebar_position: 4
---

# Vegetation, fire and weather

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`, the symbolized Linux dedicated server; addresses are the server's
unless marked Steam, which are `Dunia.dll` functions matched against it. Data read from retail 1.03:
`defaultrealtreeconfig.xml`, `defaultgameconfig.xml`, world 1's `managers.fcb` (`FireManager`), the
entity library, two decoded saves and every `landmark*` file of both worlds. Claims marked *(likely)*
were not traced to the end. Nothing here was seen in game.
:::

How the world's plants, fire and weather respond to what the player does, and what the retail game
leaves unused. RealTree's simulation and the scatter lists are described in
[Terrain, sectors and vegetation](../engine-internals/terrain-and-vegetation.md) and
[`.rtx`](../file-formats/rtx.md); the clock, storm and rain code is in
[Time of day, light and shadow](../engine-internals/time-of-day-and-lighting.md#storm-rain-and-wind).

Retail did not thin the vegetation in data: world 1 holds 2,472,738 scattered instances, 16 fewer
than the September 2008 prototype, and the `ZoneLogicManager` density values are the same in both.

## Vegetation reacting to vehicles

Only RealTree *bushes* react to a moving body, and the reaction is on in retail.

1. `CVehicleWheeledPhysComponent::Update` (server `0x090B4BA0`), `CVehicleFloatingPhysComponent::Update`
   (`0x090B2C10`), `CVehiclePhysComponent::OnMove` (`0x090B8C00`) and
   `CCharacterPhysComponent::Update` (`0x0951AC20`) call
   `CAABBPartitionManager::UpdateMovingPhysEntity` (`0x0968AF40`) every frame with the mover's
   position, velocity and box.
2. It classifies the mover: a rigid entity is type 0; a vehicle is type 0 when dry, 3 when in water
   and 4 for a paraglider; a character is type 1; an animal is type 2, and only when the quality
   level's `ResponseToAnimal` is set.
3. `CAABBPartitionManager::Update` (`0x0968DAE0`; Steam `0x105329B0`, same code) asks
   `GetRealtreeMovementList` (`0x0968D130`) for the RealTrees the mover's swept capsule touches.
   Penetration is computed only for species kind 1 (bush), 8 (grouped plants) and 9 (bush with
   collision). Every other kind gets 0 and is never registered. A penetration above 0.1 reaches
   `RTxcManager::RegisterMovingObject` (`0x09981AC0`), which clones the simulation.
4. `ApplyMvObjectReaction` (`0x09998260`; Steam `0x104752F0`) sends the player pawn, characters (1)
   and animals (2) to `ApplyMvObjectReactionLight`: the bush bends and springs back. Types 0, 3 and 4
   go to `ApplyMvObjectReactionHeavy` (`0x099977E0`; Steam `0x10474BF0`). The jungle `hy_*` plants
   have the same pair.
5. `ApplyMvObjectReactionHeavy` calls `ApplyDestructionHeavy` (`0x09995110`; Steam `0x10473FD0`) when
   the mover overlaps the bush, moves faster than **10 m/s** (36 km/h) and its velocity points into
   the bush within a cone taken from the mover's own box. Below that, or off axis, the bush only
   bends.
6. `ApplyDestructionHeavy` breaks `min(10, int(v/2.5) + int(rand·v/12))` branches. Each is launched
   at `0.9·v` (upward-biased, capped at 40 m/s) with spin `10·v` (capped at 150) through
   `RTxcManager::CreateBrokenBranch` (`0x0997D2D0`), which spawns a physical branch and plays the
   species' `Explosion` leaf burst from `realtreefx.xml`.

| Gate | Where | Retail value |
|---|---|---|
| Species kind 1, 8 or 9 | `GetRealtreeMovementList` | every `rt_bush_*`, `rt_mountainbush_*`, `rt_tree_acacia_small`, `rt_tomato_plant_a` and the jungle `hy_aloes_01/3`, `hy_araceae`, `hy_banana_big`, `hy_bigleaf` are kind 1; `rt_aremisia_groupe_*`, `rt_artemisia_a`, `rt_tomato_plant_b` are kind 8; no species is kind 9 |
| Species destructible byte (skeleton `+0x04`) | `ApplyMvObjectReaction` | 1 on every bush; 0 on baobabs, kapoks, the canopy, `rt_deadtree_a`, `rt_palm_small`, `rt_root_02` and some large diospyros and acacia |
| Mover type 0, 3 or 4 | `ApplyMvObjectReaction` | a driven car is type 0; the player on foot is never eligible |
| Speed | `ApplyMvObjectReactionHeavy` | `10.0`, a float read only here (Steam `0x10F9836C`) |
| Branches per hit | `RTxcSimulation` constructor | single player hard-codes 10 (`MOV [ESI+0x760],0xA`, Steam `0x1046C0D7`); multiplayer reads `MultiNbBrokenBranchesMaxPerdestruction` |
| Broken-branch pool | `CRTxEngineResource::CanBreakBranch` (`0x095BCA50`) | `DestructionNbMaxBrokenBranches` ([table](#the-realtree-quality-config)) |
| One destruction per tree per frame | `RTxcSimulation` fields `+0x520..0x528` | always |
| `Destruction` quality flag | `CreateBrokenBranch`, `gRTQualityConfig+5` (Steam `0x11611F68`) | 1 on every tier but Low |
| `Simulation` quality flag | `CloneSimulation`, `AdvanceTime` | 0 on Medium and Low, so *(likely)* no per-tree reaction at all |

In retail a bush bends below 36 km/h or off axis; head-on above it, up to 10 branches of a roughly
100-node bush fly off in a leaf burst. The impostor bushes (`facingbush*`, `facing_bush_*`) and all
grass are `.xbg` meshes, not RealTree, and never break. Jungle plants have no particle entry in
`realtreefx.xml`, so they break silently.

Changing it takes a data edit for the pool and the quality flags, and an FCSE plugin for the two
constants: the 10 m/s float and the immediate 10 at the branch cap. The launch speed scales with
`v`, so a lower gate also gives gentler launches.

### Characters and animals

`CCharacterPhysComponent::Update` feeds the same list as type 1, so a person on foot only bends a
bush. The `Heavy` speed gate would block walking pace even if the type were changed.

### Trees hit by a vehicle

Non-bush species have their own collision (`CTreeClusterElement::CreatePhysEntity`).
`CTreeClusterElement::OnEvent` (`0x094E7D10`) reacts to a collision stim from a renderable entity
with impulse above 1000: it plays the `VehicleImpact` effect (the `real_tree.choc.*` leaf burst and
a sound) and `RTxcManager::ApplyVehiculeCollision` (`0x09981BD0`) shakes the tree. No branch breaks
on this path.

## Bullets, rockets, blades, blasts and fire

All of these reach RealTree and are live:

| Source | Path | Effect |
|---|---|---|
| Bullet | `CRTxEngineResource::RayCheck` (`0x095C15F0`) -> `RTxcManager::ShootReaction` (`0x09981F50`) | defoliation, cut branches, shooting particle |
| Rocket in flight | `CRocket::Update` -> `RocketReaction` (`0x09981E40`) | the same family |
| Machete | `CWeaponFireMeleeStrategy::PerformCut` -> `MeleeCutReaction` (`0x09981C50`) -> `ApplyCutReaction` (`0x09987D20`) | cut |
| Blast | `CTreeClusterElement::OnEvent` on a Crush stim above level 3 -> `RTxcManager::ApplyExplosion` -> `ApplyExplosionTree` or `ApplyExplosionBush` (`0x09995820`, `0x09996830`) | branches break |
| Fire | `CFireRealtreeComponent::InitializeRealtreeFire` -> `IgniteFire`; `UpdateFire` -> `ForceBrokenBranch` | burning branches drop |

Only thin branches break. Table `RTMaxDestRadius` (server `0x0A43C620`) caps the radius: 0.055,
0.055, 0.06, 0.08, 0.025, 0.055, 0.08, 0.8 m, and so on. Shots and rockets read slot 0, explosions
slot 3 (0.06 m, `0x0A43C62C`) and wind slot 6. Trunks never break. The table is hard-coded and
readable only by patching.

No code fells, topples or uproots a tree, or removes a scattered plant: a symbol census finds only
`RTxcDestruction`'s node-level `Cut`, `RegisterBreakImpact`, `InactiveBranch` and `RestoreBranch`.
`CRTxEngineResource::ReleaseBrokenTree` (`0x095C1C40`) only calls `ReleaseBranchEntity`. Making a
tree fall needs new code and new trunk and crown assets.

## Branch regrowth

Cut branches grow back in retail, with no switch to throw. The chain, from a break to a full branch:

1. A break (`RTxcDestruction::Cut`, `RegisterBreakImpact` `0x0999EF60`) stops regrowth on the cut
   subtree and lists the branch for restoring.
2. `RTxcDestruction::Update` (`0x0999F080`) draws the fallen branch at its falling matrix and shrinks
   it: `ScaleOutUpdate` (`0x0999D410`) adds `dt · ScaleOutSpeed` to its scale-out. `ScaleOutSpeed` is
   0.05 per second in every single-player tier, so a branch vanishes in 20 s.
3. At scale-out 1, with the simulation's regen byte (`+0x619`) 0 and no child break overlapping
   (`ReadyToRestore` `0x0999D250`), `RTxcRegenNode::RegenStart` (`0x099AA120`) puts the subtree's
   nodes into a growing state with target lengths from the skeleton.
4. `RTxcRegenNode::Update` (`0x099A9BA0`) lengthens each node at its speed in m/s. Children start
   when the parent passes 0.9 of its length, or on a random roll, and leaves scale with their node.

The speed depends on the species family:

| Family | Clone function | Regrowth speed |
|---|---|---|
| Savannah `rt_*` (PRT) | `RTxcSimulationPRT::Clone` (`0x09992D20`) | 0.05 m/s, the global `gRT_GENERATION_SPEED` (server `0x0A43C664`, Steam `0x10F97F9C`); the config's `RegenSpeed` is ignored |
| Jungle `hy_*` (HRT) | `RTxcSimulationHRT::Clone` (`0x0998D1A0`) | the config's `RegenSpeed`, 0.02 m/s |

A 1 m branch therefore takes 20 s to vanish and 20 s (PRT) or 50 s (HRT) to regrow. The clone, not
the original, regrows: a tree becomes a unique simulation the moment it is shot, blasted, burnt or
pushed, and `ReturnClonedToOriginal` (`0x0997E090`) hands it back to the pristine shared original
when the budgets run out (`BudgetUnique`, `BudgetRestore`, `BudgetIdentity`, `BudgetRegen`, 500,000
on Ultra) or after `LifeTimeUniqueSim` (6 s) and `LifeTimeRestoreSim` (8 s) not rendered. A broken or
burning simulation goes through "restore" and "regen" first. In play, damage heals in tens of
seconds while watched and snaps back when the tree leaves view.

Changing it takes data (`RegenSpeed`, `ScaleOutSpeed`, the lifetimes and budgets in
`defaultrealtreeconfig.xml`; a `RegenSpeed` of 0 or less turns regrowth off for the jungle family).
The savannah 0.05 is a write to the global, or the console command `RTRegen` (see the
[developer console](../engine-internals/developer-console.md)).

### Staged growth from a sapling

`RTGenesis speed` (`CScriptObjectGame::RTGenesis` `0x088B2380`) calls `RTxcManager::StartGenesis`
(`0x0997D070`) and then, per active simulation, `RTxcRegenNode::StartGenesis` (`0x099AA490`), which
is `RegenStart(0)`: the whole tree regrows from its root node, branches sprouting in order, with
every leaf scale zeroed first. It goes through the same update path as branch regrowth. Its only
caller is the console and Lua command; no gameplay, script or data triggers it. It acts only on
trees that are already simulations.

### Burnt trees

`RTxcFire::BurnTheNode` (`0x099A0FB0`) raises a per-node char factor by up to 0.75 within 1.5 m, and
nothing lowers it. A burnt tree is pristine again only when its simulation returns to the original
or its sector unloads. Burning branches drop and enter the regrowth above *(likely; not traced end
to end)*. `CFireRealtreeComponent` saves its fire nodes, so a saved fire reloads; what it shows was
not traced.

## The RealTree quality config

`CRealtreeConfig::UpdateCurrentQuality` (`0x099AE510`) picks one of five tier blocks in
`defaultrealtreeconfig.xml` from the profile's `RealTreeProfile Quality` (Ultra, High, Medium, Low,
Legacy; any other string is Custom). The server's registrar is `0x099AE6C0`.

| Tier | `DestructionNbMaxBrokenBranches` | `Destruction` | `Simulation` | `ResponseToAnimal` |
|---|---|---|---|---|
| Ultra | 50 | 1 | 1 | 1 |
| High | 15 | 1 | 1 | 1 |
| Medium | 10 | 1 | 0 | 1 |
| Low | 5 | 0 | 0 | 0 |
| Legacy | 50 | 1 | 1 | 1 |
| Custom | 50 | 1 | 1 | 0 |

`RTxcManager::SetQualityConfig` (`0x0997CE50`) passes the pool size to
`CRTxEngineResource::SetMaxBrokenBranches` (`0x095BCA20`) through the resource's vtable. The limiter's
own default of 15 (server `0x0A1C1860`) applies only before the config loads; broken branches are
limiter category 4 ([bodies and ragdolls](./physics-and-explosions.md) covers the limiter).

The PC profile writes `Quality="VeryHigh"`, which matches no tier name
(`GetRealtreeQualityLevel`, Steam `FUN_10462280`, names at `0x10F97EEC`), so it selects Custom
*(likely; seen in one `GamerProfile.xml`)*: animals then do not bend bushes, and the pool is 50.

Two keys are dead: `BudgetManagementOn` and `FireParticleScale` appear as strings in neither the
server nor either `Dunia.dll`. `DestructionStormRadius` (10 on Ultra, High, Legacy and Custom, 5 on Medium, 0 on Low) is read by no
code, which is the gap the next section describes.

## Storm wind

Wind tearing branches off RealTree is finished in code and never triggered.
`RTxcManager::CheckWindDestruction(float wind)` (`0x09982600`) walks every live simulation. For each
it calls `WindDestHappen(wind, DestructionStormRadius)` (PRT `0x09993AA0`): a chance of
`min(ln(wind) · 0.001, 0.25)` and a count of `ln(wind) · 0.25 · rand`. It then clones the simulation
and `WindDestPrepare` (`0x09991F80`) sets the count and a 0.5 s timer. `ApplyWindDest` (`0x09994810`;
Steam `0x104745D0`, called from `PostSimulate`) breaks branches thinner than `RTMaxDestRadius[6]`
with a sideways impulse and spawns the species' `Wind` effect (the `real_tree.wind.gfxrt_wind_*`
particles; eight systems ship in both builds).

`CheckWindDestruction` has no caller in the server. On PC nothing reads
`DestructionStormRadius`: all 34 references to the quality config global (`0x11611F68`) read offsets
4 to 7, `0x10` to `0x28` and `0x34` to `0x74`, never `0x2C`. The wind particles are spawned by
nothing else *(likely; only the one caller of the effect type was found)*.

Bringing it back is new FCSE code that, on a storm or every frame, walks the simulations near the
camera and calls the existing virtuals `WindDestHappen` and `WindDestPrepare` (vtable slots `+0x54`
and `+0x58`) with the manager's wind force.

## Grass

- **Displacement.** Only vehicles carry `CGrassDisplacementComponent` (a quad primitive).
  `CDisplacement*::DisplaceGrass` (sphere `0x094D5930`, quad `0x094D4C70`, segment `0x094D6120`)
  writes 8 bytes per tuft (`CClusterBendData`: rotation rows and strength) that the `Mesh_Grass`
  `BENT` vertex shader applies. Characters and animals have no displacement primitive, so the player
  does not part grass; the component is generic, and a sphere primitive on the player archetype is a
  data edit that was not tried.
- **`bBreaksGrass`** (`CDisplacementPrimitive+4`). When set and the bend exceeds 0.75, bit 24 of the
  tuft's second word is set and every later displacement skips it, so the grass stays flattened
  instead of springing back. Every car and boat has it `True`; all `vehicle.Land.BigTruck*`
  archetypes have it `False` in both builds. Changing it is a data edit.
- **Slowdown.** `CVegetationSlowdownComponent` (`GetSlowdownFactor` `0x088D4830`, curve
  `archSlowdownCurve`, fed by `CVegetationObstructionEvent`) slows the player in vegetation, and is
  live.
- **Density.** The Windows "High" render preset maps to `GeometryQuality="medium"` in retail, where
  the prototype's maps to `high`: `LeavesRatio` 0.5 against 1.0, `ClustersLodScale` and
  `KillLodScale` 1.25, `LodScale` 1.2, `RealTreeCapsMaxDistance` 90, and `ClusterObjectMinSize` and
  `RealTreeLeafMinSize` 0.04 against 0.02. Grass and clusters cull sooner and by larger screen size
  on High; Very High and Ultra High equal the prototype's High. The renderer's use of `LeavesRatio`
  was not traced *(likely)*. The `legacy` preset's `DensityLodScale` (LightJungle 1.05, Savannah 1.2,
  Woodland 1.1) is the only per-zone thinning, and the console command `gfx_DensityLodScale`
  (`CRenderConfig::ChangeDensityLodScale` `0x0982CFE0`) changes it live. All of it is
  `defaultrenderconfig.xml`.

The `ZoneLogicManager` `DensityScale` per zone (DenseJungle 2, HOD 2, MediumJungle 1.5, Woodland
1.15, the rest 1) is read only by the editor's scatter code *(likely)*, so editing it does nothing to
baked vegetation.

## Rain and storms

The rain gate is described in [Time of day](../engine-internals/time-of-day-and-lighting.md#rain);
the numbers behind it:

- **What is rolled against.** Layer 1's cloud coverage from `UpdateSky` (`0x091601A0`; Steam
  `0x1011A380`): the base preset `Default.Clear.Cloud` (0.15) lerped toward `Default.Storm.Cloud`
  (1.0) by `Default.Transition.Cloud`. Coverage above 0.63 needs a storm factor above 0.565; above
  0.75 needs 0.706. Other layer-1 presets: Desert 0.18, Cloudy 0.88, SandStorm 0.8 (reachable only
  as overrides).
- **The storm curve is mostly calm.** `Default.StormIntensity.Default` (626 knots over 0 to 92.66
  storm hours, identical in both worlds) exceeds 0.565 only at storm hours 35.7-37.2, 58.9-61.7,
  83.7-84.5, 87.8-88.4 and 89.75-90.6, 6.4 % of the loop, and 0.706 only at 35.9-36.9, 58.9-60.0,
  60.4-61.6 and 83.9-84.3. Elsewhere it sits between 0.1 and 0.5.
- **The storm clock is slow.** Retail `TimeScale` is 6 (`defaultgameconfig.xml`; the constructor
  default is 10), so a storm hour is 10 real minutes and the loop about 15.4 hours. From storm hour 0
  the first rain window opens after about 6 hours of unpaused play.
- **Dwell.** Coverage must stay above the threshold for 180-195 real seconds before the roll, and the
  desert zone suppresses it.
- **Saves.** `StormHour` (hash `2E12B421`) is a saved member, as are the rain evaluator's timer and
  threshold. Two decoded saves hold storm hours of 0.108 and 5.44. `CDynamicEnvironmentManager::Reset`
  (`0x09159C00`; Steam `0x1011CF70`), run by `UnloadWorld` (`0x08882E60`), zeroes the storm hour, and
  the persistence restore applies after the world loads *(likely; order not traced)*, so a loaded
  save keeps its storm hour and one made inside a peak reloads inside it.
- **Unbound presets.** The two unbound `Default.Transition.Rain` presets step from about 0 to 1 at
  storm 0.70 (world 1's first copy) and 0.75 (second copy).
- **What uses the weather.** Only the clock, the storm curve, the zone weights and the scripted
  overrides feed it (`SetScriptedStormFactorOverride`, `Set{Fog,Cloud,Wind,Lighting,AdaptiveBloom,
  DepthOfField}Override`). Nothing reads the player. Mission `a3sm15` is the only one that touches the
  storm override (0.2 over 120 s, then removed).

Everything downstream of the flag works: the player's `CRainComponent`
(`environment.weather.rain`, `environment.weather.rain_splash`, `bAutoStart` False, 25 rays per
second, grid 8) is on `player.MainCharacter.PawnPlayer`. Changing when it rains takes data (reshape
the storm curve or raise the cloud coverage in both worlds' `managers.fcb`), a script calling
`SetScriptedStormFactorOverride` with 0.75 or more, or a plugin forcing the flag.

### Lightning and thunder

Neither exists. Neither build has a lightning or thunder string in its environment, render or sound
code. `graphics\gfx\atmospherics\gfx_lightning.xbt` is the texture of the electric-spark particle
`*.B_imp_electronic_stuff.lightning` (bullet and machete impacts on electronic objects), loaded by
data. `amBX_thunder` in `Dunia.dll` (`0x10F35DC0`) is one of eleven peripheral-rumble waveform
names, used once, for fire. No thunder sound cue is among the tagged sounds *(likely; the tags are
heuristic)*. Fire is not started by weather either: see the next section.

## Fire

### Spread

`CFireComponent::PropagateFire` (`0x08B4CD80`) is live and unrestricted. Per burning node and
neighbour, the spread strength is the wind term (direction and force projected on the node
direction, times force/30), plus `fFireDmg`, plus the direction's vertical component times
`fFireDmg` times 0.5, so fire climbs faster than it descends. The wetness modifier reduces it, and
`HasTheBudgetToPropagate` (`0x08B3CD20`) gates it; each new fire subtracts budget
(`SubstractFireBudget` `0x08B3D2E0`) unless `bIsBudgetIndependent` is set.

The world's `FireManager` data: total budget 400; a cost of 1 for a region, object or compound; a
cost of 0 for a RealTree (`iCostRealtree`); 40 each for Molotov, flamethrower and explosion fires.
`OnGlobalBudgetDepleted` (`0x08B3E010`) zeroes every fire's budget. The seven
`weapons.Special.FireStickyStream` archetypes have `bIsBudgetIndependent` False and `fDmgBuffer` 0,
so a flamethrower or Molotov fire competes with the world's fire for the 400 points; setting the
flag is a data edit.

Fire is started by stims only. `CFireComponent::IgniteOnStim` (`0x08B445A0`) accepts Burn (7),
Pierce (3) and Crush (4), each behind a per-object flag (`IgniteOnBurn`, `IgniteOnPierce`,
`IgniteOnCrush`), and `CCollectionIgnitorComponent::OnEvent` (`0x08B395E0`) reacts to Burn stims
against ignitor zones. No weather or environment path starts one. Every ignitor takes its
`burnableobjects.xml` profile: `bProfileOverride` is False on all 1,409 ignitors.

### Burnt ground and grass

The savannah region (`Burnable.Region.Savannah`) has `fFireDmg` 6, `fNodeFuel` 7 and
`GridResolution` 3, and a set `decalDecal` (4001733941), so a burnt-ground decal is configured
*(likely; `UpdateBurningRegion` `0x0881DDD0` not decompiled for it)*. Burning grass is a per-tuft
burn factor in the renderer's `CClusterBurnData` (`CFireRegionNode::SetBurningGrassState`
`0x08B40370`; 1 intact, 0 burnt), with a scorched-ground list in `CSceneBurnRegion`.

**Respawn is coded and switched off.** `CFireRegionComponent::SetOnFire` (`0x0881CA10`) arms
`ResetAfterBurnTime = ResetAfterBurnDelay` when the delay is above 0, and `Update` (`0x08820690`)
counts it down and calls `CFireRegionNode::RespawnGrass` (`0x08B41FD0`) on every node: burn factor
back to 1.0 at once (not in stages), node fuel and health 40. In `world1.managers.fcb`,
`ResetAfterBurnDelay` is 0 on `Region.Savannah`, `Region.Jungle`, `Region.SmallPatch`,
`Region.Tirepile` and `Region.MP`, and 15 on `Region.GasPuddle` and `Region.OilPuddle` only.
`CFireRegionComponent::RespawnGrass` (`0x0881CC50`, `0x0881CCC0`) has no caller. Without the delay, burnt grass clears only when its cluster streams out
(`OnGrassClusterDestroyed`) or on `CFireManager::Reset` (`0x08813BC0`) through
`ResetBurnedGrassVisitor`. Turning the respawn on is a value in both worlds' `managers.fcb`. Staged
regrowth needs new code, and depends on the renderer blending intermediate burn factors *(not
traced; burning itself ramps 1 to 0)*.

### Rain against fire

The raining byte is written by `CFireManager::Update` (`0x088176D0`), once per second, from the
focused terminal's `CRainComponent` (member `+0x71`): set, `CFireManager+0x201` becomes 1; clear or
missing, 0. So fire follows the natural roll and a forced rain alike, but only for the focused
player entity. While it is set, `CFireManager::SRainEffect::Update` (`0x08816E50`) every second calls
`CFireBudgetManager::DecreaseBudgetFromRainEffect` (`0x08B3CD70`) with
`BudgetDecreaseRatePerSecond`, so burning fires lose budget and die. The wetness level (index at
`CFireManager+0x1EC`, -1 dry) rises one step per `TimeToIncreaseWetnessLevel` in rain and falls one
per `TimeToDecreaseWetnessLevel` when dry. World 1's `RainEffect` values are 5 s, 5 s and 10 per
second.

Wetness is meant to slow fire, with real readers: `GetCurrentWetnessNodeBufferModifier`
(`0x0880D4F0`, from `CFireNode::ReceiveBurnStim` `0x08B42A10`) and
`GetCurrentWetnessPropagationModifier` (`0x0880E180`, from `PropagateFire`, which subtracts
`modifier · damage`). The data stubs it out: the `WetnessLevel` list holds one entry with an empty
name, `NodeBufferIncrease` 0 and `PropagationDecrease` 0, so wet ground changes nothing about
ignition or spread. Filling the list in both `managers.fcb` is a data edit.

### Fire quality and bodies

`CGameFireQualityConfig` (`defaultgameconfig.xml`): VeryHigh and High have `FireEnabled` 1 and
`FireSmokeEnabled` 1; Medium and Low have `FireEnabled` 1 and `FireSmokeEnabled` 0. The profile
default is VeryHigh and no preset disables fire, so only smoke is cut on Medium and Low (reader not
traced; *likely*). `IsFireEnabled` (`0x0880D410`) gates ignitor events.

Pawns burn through the Burn-stim damage path and the burn pool (see
[Stims and damage](../engine-internals/stims-and-damage.md)), not through a fire component.
`CFireStickyStreamComponent` exists on only three library entities (the flamethrower and Molotov
stream types, `fFireDmg` 15, a Burn stim of radius 1 and level 8, a Fear stim of radius 10 and level
4). No vehicle fire component was found in data *(not exhaustively checked)*.

### What fire does to soldiers and animals

- **Soldiers flee burning ground.** The merc brain has a live `EscapeFire` plan: a
  `CScannerFireProximity` and `CTaskCheckFireProximity` with `FireDistTrigger` 7 and
  `FireAlertNear`/`FireAlertFar`. `CSensoryStimProcessor::ProcessFirePropagationFearStim`
  (`0x08732AB0`), called from every brain state's Fear (10) handler for stims with detail
  `firePropagation`, sets flag `0x15`, the blackboard `SwitchToFireAlert`, `LastFirePos` and
  `FireAlertTimeStamp` (1 s throttle); `CScannerFireProximity::DoRun` (`0x084C3E80`) sets
  `FireIsNear` (flag `0x11`) when the distance is under the trigger. The buddy, player and
  special-character brains carry the scanner too. No arsonist search exists.
- **Animals ignore the Fear stim.** They are threatened only by Bang, Snap, Pierce, Cut and Burn
  stims ([Animals](../engine-internals/animals.md)). The fire emitter's Burn stim reaches 1 to 2 m
  and its 10 m Fear stim is ignored, so an animal flees only when the flames touch it *(likely)*.

## Uncertain

- Whether the savannah `Clone` function on PC (the read at Steam `0x1047396D`, an undefined
  function) takes the global exactly as the server does *(likely)*.
- `RTxcDefoliantNode` and `StartDefoliantForAllTrees` (`0x0997D140`), the mission defoliant, were
  not traced.
