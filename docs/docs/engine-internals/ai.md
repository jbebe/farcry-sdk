---
sidebar_position: 18
---

# AI

:::info[Verified via reverse engineering]
Traced through GhidraMCP against the symbolized `FarCry2_server`; the brain contents are read out of
the shipped `mercbrain.ai.rml`. The workspace loader, `GetChanceOfSuccess` and `ManageIntuition` were
matched in the retail PC `Dunia.dll` (Steam) and behave the same. See [intro](../intro.md) for how
RE-verified and community-reported claims are distinguished.
:::

A soldier's behaviour is split between C++ and data. C++ decides **which** behaviour runs and how
well he sees and shoots; data decides **what** each behaviour does. The class hierarchy and the
`CAIEngine` bootstrap are in [Architecture](./architecture.md#caiengine--a-classic-sensedecideplanact-architecture-group-aware);
this page is about how the pieces decide.

| Layer | Where it lives | Editable |
|---|---|---|
| Which behaviour runs | `CBrainMerc*` classes, fed by agent flags and squad orders | partly - through the flags |
| What a behaviour does | plans and tasks in [`mercbrain.ai.rml`](../file-formats/ai-rml.md) | yes |
| How far and how well he sees | `SensorySystem` of the archetype's `CPawnAgent` | yes |
| How often he hits | `ShootingSystem` of the archetype's `CPawnAgent`, and the weapon | yes |
| How likely optional tactics are | `AdaptativeBehavior` in `gamemodesconfig.xml` | yes |

All of it is editable in JackAll's **AI** tab.

## Brains and behaviours

`CBrainMerc` is a `CBrain`: a plan that, every update, asks C++ which *behaviour filter* applies and
runs the plan its `Selectable` for that filter names. The top layer's filters are the brain states:

`MercBhvIdle`, `MercBhvAlert`, `MercBhvCombat`, `MercBhvDead`, `MercBhvThreshold` (wounded and
rescue), `MercBhvSpecial` (burning, drowning, falling), `MercBhvSocial`, `MercBhvVehicle`,
`MercBhvDomino`, `MercBhvScriptedMode`

Each selects a sub-brain that does the same one level down. `CBrainMercCombat`, for instance, picks
among 41 filters: `CombatLongFireRange`, `CombatMediumFireRange`, `CombatShortFireRange`,
`CombatKillFireRange`, `CombatThrowGrenade`, `CombatPassiveLeapFrog`, `CombatRushTarget`,
`CombatHigherTarget`, `CombatNoCoverRunToThreat`, `ShootFlare`, `CombatBuilding`, `FriendDied`,
`Relocate`, `GrenadeEscape`, `EscapeFlameThrower` and others. The choice is C++; the plan each one
runs - where to move, when to crouch, how to fire, what to say - is data.

### How combat picks a behaviour

`CBrainMercCombat::DoStart` walks a fixed priority chain and hands the first match to the brain with
`CTask::DispatchConnection(brain, ANCHOR_SELECTABLE, filter)`. Most links of the chain test one of
the agent's 64 *flags* - requests, several of which the chain clears as it acts on them. Flags are set
by C++ (squad orders, senses) and by the brain itself through `CTaskOperateOnFlagField`
(`FlagFieldValue` is the index), so a plan can ask for any of these on the next update:

| Flag | Selects | Set by |
|---|---|---|
| 14 | `ShootFlare` | squad order `SendFlare` |
| 15 | `CombatSelectBestTarget` | `SetNewBestTarget` tasks |
| 16 | `GrenadeEscape` | `CPawnAgent::EscapeThreat`; a vehicle's `CommandEscapeProjectile` |
| 17 | `SwitchToFireAlert` | `CScannerFireProximity`, the threat search |
| 19 / 59 | `EscapeVehicle` / `EscapeFriendlyVehicle` | `CPawnAgent::CheckIncomingVehicle` |
| 25 | `MercBhvHurt` | `SetPlayHurt` |
| 32 | `MountedWeapon` | the `UseMountedWeapon` plans |
| 43 | `CombatRushTarget` | squad order `RushTarget` |
| 45 | `Relocate` | `SetNeedToRelocate`, the `ScanRelocate` scanners |
| 53 | `CombatRushClashPoint` | the squad lieutenant, only in the Town Escape mission |
| 55 | `CombatNoCoverRunToThreat` | the `NoCoverMoveAttack` and `NoCoverOrNoPathFailure` plans |
| 56 | `CombatHigherTarget` | `SetTargetHigher` in the `CheckHeightDiff` plans |
| 57 | `FallBackToStartPosition` | squad order `Fallback`, `SetFallback` tasks |
| 61 | `ScriptedShootAtTarget` | Domino |
| 63 | `ShotByAnotherTarget` | `CSensorySystem::SetShotByTarget` |

Two of these are narrower than their names suggest. `CDispatcherSquadLieutenant::CheckSpecialMissionBehaviour`
sets 53 on every squad member without a long-range weapon, and only while the army's mission type is 6.
The story-mission scripts set that type: 1 Defence Reversal, 4 Buddy Betrayal, 5 Barge Assault,
6 Town Escape, 7 Dental Plan, 8 Jack's Buddy. So the rush to a clash point is the Town Escape set
piece and nothing else. `SetShotByTarget` sets 63 only when the shooter is the player and the
soldier's current target is someone else or no one. He must also be in combat (army-member states 3-5)
or alert after combat (state 2 with flag 7). The branch turns him onto the player.

C++ also sets and clears flags that no filter tests, so they only matter to the plans that check them:
`CTaskShoot` raises 50 while firing and sets 49 when it gives up on a blocked shot; `SetIsInPathFollow` sets 54;
the smart-terrain executor sets 60; each brain's `DoStart` sets 26. `CBrainMercThreshold` clears
most of the combat flags when a wounded soldier recovers.

Links without a flag are situations: first contact (`CombatFirstTime`), starting from social,
trespass or a vehicle, sniping, a building, reload, target too close, the range bands below, and
`CombatNothing` as the fallback. The flag meanings are read off the names of the tasks that set and
test them; the chain's order and the flag checks are read out of the code.

A plan starts the tasks wired to its `OnStart` anchor and each task's exits (`Success`, `Failure`,
class-specific ones) start the next. Scanners are tasks that watch a condition and fire an exit when
it changes; they interrupt plans the same way. Tasks share state through the agent's *blackboard*
(`CFact<…>` values such as `BestTarget`, `NeedReload`), set by `CTaskUpdateBlackboard` and read by
`CTaskCheckFactExist` / `CScannerFactExist`.

### Range bands

`CPawnAgent::FindFireRange` buckets the distance to the target, rounded to whole metres:

| Band | Distance |
|---|---|
| kill | ≤ 12 m |
| short | 13-25 m |
| medium | 26-35 m |
| long | 36-50 m |
| none | > 50 m |

`ComputeCurrentFireRange` computes it only in the combat states (army-member states 3-5), both for
the target's current position and for its blackboard `BestTargetLastPosSeen`. The bands are
constants in code.

## Squads

Every army is run by a `CDispatcherSquadLieutenant`, a C++ commander above the individual brains. It
tracks its members and their targets, and orders them through `CEventMercCommand*` events
(`SetSquadRole`, `SetSquadAction`, `SquadImmediateAction`, `RushTarget`, `CombatTarget`,
`SetRallyPoint`, `SendFlare`, `LaunchGrenadeInBuilding`, `Fallback`, `LastManStanding` and more).
It decides who suppresses (`IsNeededAsSuppress`, per range band), when a member switches to assault
(`IsSwitchToAssaultNeeded`), runs rush patterns, picks who fires the flare, and calls in
reinforcements.

`FindRoleDistribution` splits a squad of *n* between its two combat roles by difficulty: on the
lowest setting one member takes the first role and the rest the second; on the two middle settings
70 % of the squad takes the first role, on the highest 90 %. The soldier brains never read the roles
directly - `CTaskCheckSquadRole` and `CTaskCheckSquadAction` exist but are unused - so the roles act
only through the orders and flags the lieutenant sends.

## Seeing

The archetype's `SensorySystem` gives a focus cone and a wider peripheral cone per region type -
`DesertFOV`, `SavannahFOV`, `JungleFOV`, each with `fLength` and `fAngle` - scaled by
`FOVMultipliers` for the situation (unaware, in combat, after combat, player in a vehicle, night,
looking through a scope).

Inside the cones, `VisibilityEvaluatorParameters` weigh how visible the player is from 0 to 1:
distance, cone edge, body coverage, occlusion, vegetation, stance, movement and ambient light, each
with its own weight.

That visibility is compared with two thresholds that depend on the brain state.
`CPawnAgent::SetVisibilityValues` installs them whenever the state changes:

| State | Field pair | Soldier default |
|---|---|---|
| idle | `m_IdleFuzzyVal` / `m_IdleClearVal` | 0.3 / 0.75 |
| social | `m_SocialFuzzyVal` / `m_SocialClearVal` | 0.3 / 0.75 |
| alert | `m_AlertFuzzyVal` / `m_AlertClearVal` | 0.25 / 0.6 |
| combat | `m_CombatFuzzyVal` / `m_CombatClearVal` | 0.2 / 0.4 |
| threshold | `m_ThresholdFuzzyVal` / `m_ThresholdClearVal` | 0.3 / 0.75 |
| special, dead, vehicle | `m_Special…`, `m_Dead…`, `m_Vehicle…` | |

*Fuzzy* is where he starts to sense something, *clear* where he positively sees the player. One
caller path lowers *clear* to *fuzzy*, so the first hint is already a sighting.

### Intuition

Whenever a soldier (re)acquires his target - `UpdateBestTargetInfo`, a squad `CombatTarget` order,
or the `CTaskSelectBestTarget` task - `ResetIntuitionTimer` starts a window. For the next **6
seconds**, in the combat states, `CPawnAgent::ManageIntuition` copies the target's true position and
velocity into `BestTargetLastPosSeen`, `BestTargetLastPosSeenHigh` (1.5 m higher) and
`BestTargetLastVelSeen`, seen or not. Breaking line of sight therefore does not break the track for
six seconds; searching starts from where the player *is*, not where he was last seen. The 6 is a
constant in code (`Dunia.dll` Steam: the `MOVSS` at `0x1096e83b` reads it from a shared constant).

## Shooting

Whether a shot may hit is decided by `CShootingSystem::GetChanceOfSuccess`, then
`GenerateShootAtPosition` / `GenerateMissedShotPosition` place the bullet. For a soldier (army 0 or
1) the chance is the product of:

- **Shooter status** - his own stance (`fStandingFactor`, `fCrouchingFactor`), `fIronsightFactor`,
  and his speed band (baby step, walk, jog, run, sprint), or `fSwimmingFactor`.
- **Target status** - the same factors for the target, with `fDrivingFactor` scaled down by the
  vehicle's evasiveness.
- **Weapon range** - `CWeaponPropertiesCommon::GetTargetDistanceFactor` at the target distance
  (clamped to 220 m); forced to 1 when he aims with a sniper rifle.
- **Group number** - the curve named by `archGroupNumberCurve`
  (`Curves.ShootingSystem.GroupNumber`), evaluated at how many AIs currently shoot at the same
  target.
- **Timer to miss** - 0 until `fTimerToMissTarget` seconds after the timer starts, 1 after it.
  Every shot misses before then.
- **Hit cap** - 0 once the target has taken `uiMaxHitPerSecondFactor` hits in the current second.

Inside `fPointBlankDistance`, once `fTimerToPointBlank` has run out, a soldier's chance is simply
1: every shot hits, with no timer to miss, no hit cap and no forced misses.

`GenerateShootAtPosition` then rolls **the average of three uniform random numbers** against the
chance. The average clusters around 0.5, so the roll exaggerates both ends: a chance of 0.7 hits
about 88 % of the time, 0.3 about 12 %.

**Forced misses.** After a hit, beyond point blank, the shooter misses the next *N* shots outright.
*N* is drawn between two fields of the weapon's `CWeaponPropertiesCommon`, per difficulty -
`nForcedFailureMin`/`nForcedFailureMax` × `Causal`, `Experimented`, `Hardcore`, `Infamous` - using the
game's difficulty when the target is the player and the normal values otherwise. The AK-47 ships
4-8, 2-4, 1-1 and 0-0: on Infamous nothing breaks up a run of hits. The same properties name the
weapon's range curve (`archTargetDistanceCurve`, e.g. `Curves.ShootingSystem.DistanceAccuracy_AK47`)
and `archSuccessfulHitCurve`.

Missed shots land in a `fMissWidth` × `fMissHeight` box around the target.

`GetDifficultyFactor` and `GetProgressionFactor` are multiplied in but return 1 in both builds (the
PC build inlines them as constants). Difficulty reaches accuracy only through the forced misses.

### Where difficulty acts

| What | How |
|---|---|
| Accuracy | forced misses per weapon (`nForcedFailure…`) |
| Squad roles | `FindRoleDistribution`'s split |
| Everything else in this page | not at all - archetypes, cones, thresholds and behaviour odds are the same on every setting |

## Adaptive behaviours

Some optional tactics are rolled against a percentage. `CFCXGameplayManager::GetBehaviorChancePercentage`
looks the behaviour up in the `AdaptativeBehavior` table of `gamemodesconfig.xml` and reads the
column for the level the Weapons service reports - the same 0-27 progression level that selects the
enemy weapon packs, not the difficulty setting. The twelve shipped behaviours:

`Grenade`, `GrenadeAndBuilding`, `ChaseWithVehicle`, `ReachSniperWithVehicle`, `MountedWeapon`,
`ShootFlare`, `ShootInterestingObject`, `RescueVictim`, `RangeWeapon`, `VehicleChaseLevel2`,
`VehicleChaseLevel3`, `LongRangeVehicle`

## Implemented but unused

The task factory registers every class below, and no shipped brain instantiates any of them:

- **Squads:** `CTaskCheckSquadAction`, `CTaskCheckSquadRole`, `CTaskCheckArmyRoleAction`,
  `CScannerArmyMemberRole` - the brain side of the [squad orders](#squads), which the lieutenant
  sends but no plan branches on.
- **Awareness:** `CTaskCheckVisibleByPlayer`, `CTaskCheckSeeFriendNearby`, `CScannerSideLookOpening`,
  `CScannerAgentAimingAt`, `CScannerAgentHasRaisedWeapon`, `CScannerAgentStaredown`,
  `CScannerAgentSocialProximity`.
- **Positions:** `CTaskFindProtectionPoint`, `CTaskUnReserveCover`, `CTaskCheckCoverDist`,
  `CTaskGetPosOnNavMesh`, `CTaskCheckObstaclesInRegion`, `CTaskCheckQueryRange`.
- **Strategy:** `CTaskAttackStrategy`, `CTaskFireStrategySelector`, `CTaskMoveStrategy`,
  `CTaskChase`, `CTaskNextWeapon`, `CTaskSetForcedLookAtEntity`, `CTaskSetPostureAttribute`.
- **Brains:** `CBrainBlackboardSelector`, `CBrainDrone`, `CBrainRescueBuddy`, `CBrainSmartTerrain`,
  `CPatrolBrain`.

Others ship only in the buddy and special-character brains, never in a soldier's:
`CTaskCoverAttack`, `CTaskCheckUsingCover`, `CTaskCheckTargetRange`, `CTaskCheckThreatDistance`,
`CTaskCheckCombatMercInRadius`, `CScannerAgentIsVisible`, `CScannerVisualThreat`.

## Task parameters

A task's parameters are not declared in `RegisterProperties`. Its `LoadFromXML` reads each one by name
through the workspace serializer, after its base class's `LoadFromXML` has read the inherited ones. The
getter it calls gives the type: int, uint, float, bool, string, or fact (a blackboard fact name).
Some parameters are groups: a position is a `RefType` plus a `RefName`, and a few tasks read
`Value`/`Enabled` pairs (`CTaskComputeProjectileTrajectory`'s `MaxFlyingTime`) or one bool per
sense (`CScannerPawnSenses`). Every class's list is in JackAll's `assets/ai_tasks.tsv`. It covers
every class the shipped brains use, including the unused ones above.

A parameter is looked up by the CStringID of its exact name, so case matters, and a name the class
does not read is silently ignored. The shipped brains have one such typo: 799 of the soldier brain's
`CTaskUpdateBlackboard` nodes set `UpdateWho`, but the task reads `updateWho`, so every one of them
runs with the default. `jackall-cli ai lint <brain.ai.rml>` lists these.
