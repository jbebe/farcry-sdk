---
sidebar_position: 5
---

# Physics, explosions and destruction

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`, the symbolized Linux dedicated server; addresses are the server's
unless marked Steam, which are `Dunia.dll` functions matched against it. Data read from retail 1.03:
the entity libraries of both worlds and the Steam patch override, every campaign
`worldsector*.data.fcb`, `logicmaterials.xml` and `PhysicConfig`. Claims marked *(likely)* were not
traced to the end. Nothing here was seen in game.
:::

How hard things move when shot, blown up or driven into, and what the engine holds back. The engine
has no dismemberment, no vehicle deformation and no blast knockdown of a living pawn, so there is
nothing of those kinds to switch on. What it has is hard-coded dampers and caps, a few data values
left at their off setting, and breakable content that is built but not placed. Plants and fire are in
[Vegetation, fire and weather](./vegetation-fire-weather.md); damage rules are in
[Stims and damage](../engine-internals/stims-and-damage.md).

## Bodies and ragdolls

### A bullet pushes a ragdoll at a fraction of its impulse

`CPhysRagdollEntityImpl::ApplyImpulseOnBone(bone, impulse, tweak)` (server `0x09B14E90`; Steam
`FUN_104B3C50`) scales the impulse when `tweak` is true by
`(boneMass · tweakFactor · tweakMassMult + (1 − tweakFactor)) · tweakScale`, then
`CapLinearVelocity` (`0x09B13420`; Steam `FUN_104B3420`) clamps the bone's speed.
`ApplyImpulseOnBoneAtPos` also calls `CapAngularVelocity`. The six statics:

| Static | Value | Server | Steam |
|---|---|---|---|
| `m_doRaysOnBones` | 1 | `0x0A43C900` | `0x10F98870` |
| `m_maxLinearSpeed` | 20.0 m/s | `0x0A43C904` | `0x10F98874` |
| `m_maxAngularSpeed` | 10.0 rad/s | `0x0A43C908` | `0x10F98878` |
| `m_impulseTweakFactor` | 0.75 | `0x0A43C90C` | `0x10F9887C` |
| `m_impulseTweakMassMult` | 0.1 | `0x0A43C910` | `0x10F98880` |
| `m_impulseTweakScale` | 0.4 | `0x0A43C914` | `0x10F98884` |

Their only readers are the two functions above. `CCharacterPhysComponent::ApplyImpulse`
(`0x09514940`, bullets) passes `tweak` true; `CCharacterPhysComponent::ApplyExplosion` (`0x09514A20`,
blasts) passes false, so a blast is capped at 20 m/s but not scaled. Per bone the blast push is
`(1 − d/radius) · fPhysImpulse` (`CPhysExplosionStim::GetRagdollReaction`, `0x09562FC0`).

A bullet's `fPhysImpulse` (AK-47 30.5, M16 50, Dragunov 60, AS50 100, PKM 29.5, shotguns 25 per
pellet) therefore reaches the hit bone as `impulse · (0.075·m + 0.25) · 0.4`: 0.40 of it on a 10 kg
torso bone (about 1.2 m/s for the AK) and 0.19 on a 3 kg forearm. The values are `.data` floats with
no data key and no console variable; changing them takes an FCSE plugin writing the floats, with no
hook. The prototype's `fPhysImpulse` values equal retail's.

### A living soldier gets no push; a dying one keeps the last four steps

`CCharacterPhysComponent::OnEvent` (`0x09516E70`) stores a bullet-hit or explosion stim on a pawn
whose physics driver (`+0x158`) is not the ragdoll, instead of applying it (`StoreImpulse`
`0x09515000`, `StoreExplosion` `0x09514D70`). Stored events are applied by `SetDriver` through
`ApplyStoredEvents` (`0x095152A0`), and only those younger than `CPhysWorld+0xAC − 4` steps
(`m_eventMaxIterationAge`, 4 at `0x0A1A9AB4`, inlined). So a bullet or blast never shoves a standing
soldier, and the shot that kills him pushes the corpse only if the ragdoll starts within four physics
steps. Whether retail deaths routinely start later, for instance after the death clip of
`CPawnBeautifierRagdoll` (`Activate` `0x0900D140`), was not traced. A larger age is a patch of the
inlined 4; a push on a living pawn needs new code.

### The hurt lean

A hit on a soldier plays a procedural lean, not a push. `CProceduralRecoil::UpdateProceduralHurt`
(`0x09019CE0`) sits in `CPawnBeautifierThird` at `+0xA4`; `OnEvent` (`0x090105B0`) triggers it on any
damaging stim of level above 0 that the counters component accepts, and
`CPawnBeautifierVehicle::OnEvent` (`0x09016000`) does the same for passengers. The lean is random:
pitch −9 to −4°, yaw ±9°, roll ±12°, in at 9 per second and out at 8. The values are archetype
properties (`hurtleanPitchMin` through `hurtSpeedOut`) with constructor defaults (`0x09019760`), and
the two archetypes that set them use the defaults. Larger angles and a slower out-speed are a data
edit.

### Body bumps

`CCreatureSoundAndFXComponent::HandleCollision` (`0x088343A0`) compares impact speed with
`fSmallCollisionSpeed`, a medium threshold and `fBigCollisionSpeed`. All three are 0 on every AI
(183), animal (20) and player (26) archetype, so any contact above 0 takes the big branch: a bump
plays the big collision sound at any speed, and the small and medium sounds never play. Vehicles
default the big threshold to 8 (`CVehicleType` constructor). Setting the thresholds is a data edit
and needs the creature to have sounds for the three slots (not checked).

### Guards shoving the player

The code and data exist and no brain uses them. `CTaskPushPlayer` (constructor `0x083670E0`,
`StunPlayer`), `CPawnPushPlayerEvent` with three push types and `CFCXAIBehaviorService::GetPushImpulse`
(`0x087D10E0`) read `LightPushImpulse` 3, `MediumPushImpulse` 4 and `HeavyPushImpulse` 6 (and
`…InterpSpeed` 4) from `gamemodesconfig.xml`. `specialmercbehaviors.gosm.xml` has a `PushPlayer`
state playing `Pawn_Sync_Gesture_PushAway`. `CTaskPushPlayer` and `LightPushImpulse` are strings in
Steam `Dunia.dll` too. None of the 14 retail `scripts/game/newbrains/*.ai.rml` instantiates the
task, so the push is unreachable; using it is a brain-data edit adding the task to a plan. How the
player is moved and stunned was not read line by line *(likely)*.

### Not present

A search for `Dismember`, `Decapit`, `Sever`, `Gore` and `Gib` finds nothing. A blast never knocks a
living pawn down and lets it get up: the ragdoll is entered on death, on the rescue fall, and in
multiplayer on revive (`CPawnNetworkComponentHostImpl::DoEnterRagdoll`). The only camera shakes are
weapon fire and the blast shake described in [First-person aiming](../engine-internals/first-person-aiming.md).

## Loose objects and debris

### The dynamic entity limiter deletes, it does not freeze

`CPhysConfig::UpdateCurrentQuality` (`0x096B77C0`) copies the selected `quality` row of `PhysicConfig`
and calls `CDynamicEntityLimiter::SetMaxEntities(type, n)` with the `…0` keys only. Retail's VeryHigh
level (the one `QualitySetting` selects) sets:

| Type | List | Key | Cap | Fed by |
|---|---|---|---|---|
| 0 | simple physics objects, broken compound pieces | `MaxSimplePhysObject0` | 30 | `CCompoundPhysComponentStateNode::GoToState` (`0x09550590`), importance from `CalculateLimiterImportance` (bounding box; importance 0 or less is never limited) |
| 1 | pawn ragdolls | `MaxPawnRagdoll0` | 10 | `CAIWorld::EmptyAIDeathRow` (`0x0854DA40`, importance 1.0), unless the camera is on the body |
| 2 | animal ragdolls | `MaxAnimalRagdoll0` | 10 | |
| 3 | pickups | `MaxPickup0` | 20 | `DropWeapon`, `DropGadget` |
| 4 | broken tree branches | `DestructionNbMaxBrokenBranches` | 5 to 50 by RealTree tier | see [Vegetation, fire and weather](./vegetation-fire-weather.md) |

Over a cap, `CDynamicEntityLimiterList::FindAndRemoveEntity` (`0x096B61A0`) picks the entity with the
lowest importance (`CalculateInvImportance`: distance from the camera and age) and has
`CEntitySystem::RemoveGenericEntity` delete it. The eleventh corpse removes one; the 31st piece of
debris removes one. The prototype's rows are the same under the name `Ultra`.

The defaults before any config loads are `ms_defaultMaxEntities` = {30, 10, 10, 20, 15} (server
`0x0A1C1860`, Steam `0x10E749EC`) and an age-out of 15.0 s. Only `MaxSimplePhysObject1` is ever used:
`CEntitySystemService::Link` (`0x091BDA40`) switches type 0 to it and enables the 15 s age-out when
the service's byte `+0x3C` is set, and `UnLink` (`0x091BD990`) restores `…0`. The constructor leaves
the byte at 0 and no single-player config sets it, so it is likely the network flag.
`MaxPawnRagdoll1`, `MaxAnimalRagdoll1` and `MaxPickup1` have no reader. Raising the caps for 0 to 3
is a data edit of `PhysicConfig`; the branch list is a plugin write to the type-4 list's maximum
(`CDynamicEntityLimiter+0x54+0xC`).

### Pushing props

`CPhysBulletHitStim::GetReaction` (`0x09562E80`) and `CPhysExplosionStim::GetReaction` (`0x095630C0`)
go through `CPhysComponent::ScaleImpulse` (`0x09558D40`): the impulse times `mass/15`, times
`(200/mass)^1.5` above 200 kg, then times the object's `Stim/fBulletImpulseScale` or
`fExplosionImpulseScale` (`CPhysStimSettings`, `RegisterProperties` `0x095637F0`). A blast with a hit
count uses `sqrt(hits/maxHits) · impulse`, clamped to `sqrt|impulse| · impulseStrengthToMaxSpeed · mass`
(`maxHits` 150.0 at `0x0A43BFAC`, `impulseStrengthToMaxSpeed` 1.0 at `0x0A43BFA8`).

Every prop up to 200 kg gets the same speed change from a bullet, `fPhysImpulse/15` (2 m/s for an
AK); heavier ones get much less. Across the archetype blocks the scale is 1 on 5,548, 0.3 on 139,
0.5 on 124 and 0 on 46, the same as in the prototype. Changing it is data (the scales and
`fPhysImpulse`) or a plugin patch of the 15 and 200 kg constants inline in `ScaleImpulse`.

### Breakables throw no debris

`CCompoundPhysComponentStateNode::GoToState` (`0x09550590`) copies the state's explosion
`fPartsSpeed` into the finalize parameters when a state change carries an explosion. The entity
library's values are 0 on 303 of 339 explosion blocks, 5 on 10, 10 on 9, 15 on 8 and 25 on 5, so the
pieces of most exploding barrels, tanks and crates fall where they break. That the field is the
pieces' initial speed is read from its name and its place in `SFinalizeParams` *(likely)*. Changing
it is a data edit, limited by the type-0 cap above.

## Explosions

### No light

`CExplosive::SpawnExplosionFX` (`0x09023440`) calls
`CMuzzleFlashManager::RequestMuzzleFlash(ExplodePosition, fRadius, fDuration, fIntensity, clrColor)`
when `bUseLight` is set: the same dynamic scene light as the [muzzle flash](../engine-internals/muzzle-flash.md).
`CExplosive/Particles/Light/LightExplosion/bUseLight` is False on all 43 explosive archetypes in the
single-player libraries and the patch, `fRadius` is 0, and `clrColor` is `3.39615E+38` in every
component, an uninitialised value: the block was never authored. `LightRemains`
(`SpawnRemainFX` `0x090271A0`) is on for two archetypes only (radius 30, 0.06 s, intensity 5, warm
white). Lighting explosions is a data edit per `CExplosive` archetype: `bUseLight` True, a radius,
duration and colour. The muzzle-flash manager holds one light at a time, a newer request replacing
the older, so two blasts in the same instant share one light *(likely; not tested)*.

### Nothing punches through walls

`CExplosive::SetExplodePosition` (`0x09022EF0`) returns at once when `fPenetrateDistance` is 0.
Otherwise it casts a ray of that length along the projectile's facing and one back, and when both
hit, the obstacle is thin and the explosion point moves to its far side. `fPenetrateDistance` is 0
on all 43 explosives, so a rocket or grenade bursts on the face of a shanty wall, door or fence. A
value of 0.5 to 1.0 m on the rockets is a data edit.

### Occlusion and the Havok world

`CExplosive::StartExplosionOcclusionAndStims` (`0x09027DB0`) caps the occlusion test radius at 15 m
in code; it is not a data value. `CPhysExplosionManager::TryStartExplosion` (`0x09AE8420`) runs one
explosion's occlusion and stims at a time. `CPhysWorldImplBase::Init` (`0x09B2EE00`) leaves Havok at
its stock settings: `setupSolverInfo(5)` (`SOLVER_TYPE_4ITERS_MEDIUM`), collision tolerance 0.1,
broadphase size from config, and multithreaded simulation when worker threads exist.

### Fire from blasts

Explosion fires are charged 40 to the 400-point fire budget, as are Molotov and flamethrower fires;
the flamethrower and Molotov stream archetypes are not budget-independent. See
[Vegetation, fire and weather](./vegetation-fire-weather.md#fire).

## Bullets

Penetration is live and data-driven. `CWeaponFireBulletStrategy::ShootBullet` (`0x08F53030`) casts
one delayed ray with up to 5 hits. `ApplyDelayBullet` (`0x08F5A3E0`) walks them, subtracts each
material's `iPiercingResistance` (`CMaterialFx+0x1C`, 100 when the material is unknown) from the
bullet's raw impact-stim level, sends an impact stim at each, and stops at 0 or less. A physics
entity owned by a tree (`CPhysOwner` kind 1) stops the bullet outright.

| Material | Resistance |
|---|---|
| bush, grass, glass, straw, fence | 1 |
| flesh, head | 6 |
| light wood, light metal | 10 |
| sheet metal | 7 |
| hard wood, soft ground | 25 |
| concrete, hard metal | 29 |
| hard ground | 30 |
| mud, sand, pebble | 100 |

These are `databases/materials/logicmaterials.xml`, equal in the prototype. An AK-47 (impact level
13) passes one body (13 − 6 = 7) and stops in the second. The values are the lever.

## Vehicles

[Vehicle physics](../engine-internals/vehicle-physics.md) covers parts and collisions. What is left
off or never selected:

- **Switches.** The `CVehiclePhysComponent` constructor (`0x090A9AA0`) sets impulses (`+0x104`),
  removable parts (`+0x103`) and reliability (`+0x102`) to 1. `SetImpulsesEnabled` (`0x090BAC10`),
  `SetPartsRemovable` (`0x090BABF0`) and `SetReliabilityEnabled` (`0x090BABD0`) have no callers, so
  nothing turns them off.
- **No deformation.** No code or string models a body deforming; only terrain erosion and facial
  deformation turn up.
- **Detach impulse.** `CPart::GotoStateIndex` (`0x090B5500`) reads `bImpulseOnDetach`. It is False on
  every boat part (35) and paraglider part (9) and True on some land-vehicle parts, so boat and
  paraglider parts fall off without a shove.
- **Steering.** `CPhysWheeledVehicleEntityCreateParams` (registrar `0x09AE3440`) holds a `Timed`
  group, chosen when `bUseTimedSteering` is set, and a `Normal` group with `fMaxSteer` and
  `fMaxSpeedFullSteeringAngle`. `bUseTimedSteering` is True on all 73 single-player wheeled
  vehicles, so the direct model is never selected. What it would feel like was not traced.
- **Grass.** `bBreaksGrass` is False on every BigTruck ([grass](./vegetation-fire-weather.md#grass)).

## Buildings and breakables

A `CCompoundPhysComponent` is a state machine of named parts; its state nodes, stim masks and
events are described under [Breakable props](../engine-internals/stims-and-damage.md#breakable-props).
This section is what the campaign data does with it, counted over every campaign sector.

### What breaks

- **Parts, not shells.** 75 of the 77 `OA_Buildings` archetypes are compound breakables, but they are
  parts: roofs (`Hut*Roof*`, `StrawRoof*`), wall panels (`Savanah_Hut*WallWood*_BK`,
  `JUNGLE_Hut*Wall*_BK`), awnings, kiosks, shelters, gazebos, doors, windows and plates. Hut and house
  bodies are placed as plain meshes with no archetype *(likely)*.
- **Whole shacks that collapse.** `OA_Buildings.Buildings.ShantySm01Dest_01` (20 placements), `_02`
  (4) and `ShantySm02Dest_02W2_BK` (25): two states, intact to five debris bodies.
- **Multi-piece walls.** `ShantyMd01DestWall_01` (6 nodes), `Ethiopian_RuinsWall0x_BK`,
  `WallEroded07/10_BK`, `FenceDustTownDest_B/C` and `WoodenWagonWall_BK`.
- **The Leipzig watchtower.** `object_archetypes.Infrastructures.WatchTowerLeipzig`, named for the
  2007 demo, is placed 9 times: one breakable node, 18 plank parts (`PrimRempart*`,
  `RempartPlanche*`) around a static core, 100 health each. Its `StaticStims` mask is all off and its
  `MovingStims` is Pierce and Crush. `CCompoundPhysComponentBreakableNode::Init` (`0x09537770`) hands
  a part the static mask while it is static and the moving mask once it moves, so bullets and blasts
  do not hurt attached planks; only a collision that knocks a part loose (`WillCollisionRemoveStatic`
  `0x09540090`) can start it *(likely; the static or moving test was not traced to the part type)*.
  Turning the static mask on is a data edit.

### What holds it back

- **Masks.** Roofs, straw shelters, kiosks and `ShantyShelter*` have Pierce off in one of their two
  masks and Burn off (`PFCTBF` and `PTCTBT` pairs). The whole shack `ShantySm01Dest_01/_02` is Crush
  only (`PFCFBF` and `PFCTBF`): explosions and impacts break it, rifles and fire do not. Burn is off
  on most structural pieces, which is why buildings do not burn down. How the masks play out was not
  seen *(likely)*.
- **Two states.** 679 of 954 compound archetypes go from intact to broken in one step. Each roof or
  wall is its own entity, with no collapse cascade across entities: `BuildConnectivity` and
  `RemovePartsConnectivity` work only inside one breakable node.
- **Debris.** At most 30 live pieces, and most explosion blocks give them no speed (see above).
- **Persistence.** No state beyond the entity's own saved state was found for broken parts *(not
  traced)*.

### Built and never placed

Compound building archetypes that no campaign sector places:

- `OA_CoverObjects.DogonWallDestroy` and `TolloyWallDestroy`: 7-node walls with staged health 5, 10,
  25 and 100, the most razeable walls in the game;
- `object_archetypes.Infrastructures.BreakableOutPost01` (18 parts) and `WatchTower01` (6 nodes);
- `BridgeWood03`, `NativeGTowerStrawRoof_BK`, `DustTownWall_B/C/D_BK` (D has 5 nodes),
  `HutMd01WallWood_03_BK_Part0x_TEMP`, `Savanah_HutSm01WallWood_03_BK`, several hut roofs,
  `Kiosk_W1_Small_B`, `SmallStraw_BK`, `MediumStraw_FX01_BK` and the straw fences
  `FenceStraw0xLo_01_BK`.

Other leftovers:

- `OA_Burnable.Burnable.CHANGE-AOBUILDING-*` (straw roofs and shelters) have a two-state compound
  descriptor but static graphic and physics components, and are never placed. The `OA_Buildings`
  twins are the live ones.
- `IGE_Archetypes.AutoGen.*`, the map editor's palette, holds 55 archetypes (among them
  `ShantySm01Dest_01` and the hut roofs) with a two-state descriptor but only static components.
- `CDestructibleBridge` is an entity class with only its RTTI, `CreateObject`, `GetUpdateFlags` and
  destructors (`0x088925C0` to `0x08899610`): no logic and no data anywhere.

Placing the unplaced walls and towers (sector fragments), flipping `bPierceStim` and `bBurnStim` on
roofs, shelters and shacks, giving the Leipzig tower a non-zero `StaticStims` mask, raising
`fPartsSpeed` and raising `MaxSimplePhysObject0` are all data edits. Whole houses collapsing needs
new assets (shells cut into compound parts with `.hkx` bodies); the break itself needs no code.

### Rocks and trees

No rock is destructible. Of 298 rock models none is breakable, and scattered rocks are collection
instances with no entity and no damage path. The only rock with a compound body is
`OA_MissionObjectives.MissionObjectives.CrumblingRock_BK` (two states, health 100, every stim flag
off), placed once in `w2_d_4/landmarkfar_1901`: a scripted set piece broken by an event, not by
damage *(likely)*. Trees do not fall; see [vegetation](./vegetation-fire-weather.md#bullets-rockets-blades-blasts-and-fire).

## Uncertain

- Which body type a Leipzig plank counts as at spawn (`CBreakablePart` virtuals `+0x18` and `+0x5c`).
- Whether retail soldier deaths usually start the ragdoll within four steps of the hit.
- Whether any piece-spawn code uses `fPartsSpeed` as a speed rather than another quantity.
