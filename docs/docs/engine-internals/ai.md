---
sidebar_position: 18
---

# AI

:::info[Verified via reverse engineering]
Traced through GhidraMCP against the symbolized `FarCry2_server`; the brain contents are read out of
the shipped `mercbrain.ai.rml`. See [intro](../intro.md) for how RE-verified and community-reported
claims are distinguished.
:::

A soldier's behaviour is split between C++ and data. C++ decides **which** behaviour runs and how
well he sees and shoots; data decides **what** each behaviour does. The class hierarchy and the
`CAIEngine` bootstrap are in [Architecture](./architecture.md#caiengine--a-classic-sensedecideplanact-architecture-group-aware);
this page is about how the pieces decide.

| Layer | Where it lives | Editable |
|---|---|---|
| Which behaviour runs | `CBrainMerc*` classes | no - C++ |
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

Inside `fPointBlankDistance`, once `fTimerToPointBlank` has run out, the status, weapon and group
factors are skipped: only the timer to miss and the hit cap remain.

Missed shots land in a `fMissWidth` × `fMissHeight` box around the target.

`GetDifficultyFactor` and `GetProgressionFactor` exist and are multiplied in, but both are compiled
to `return 1`: difficulty and campaign progress have no say in accuracy.

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
  `CScannerArmyMemberRole`. `CPawnAgent` has the matching orders (`CommandSetSquadRole`,
  `CommandSetSquadAction`, `CommandSetLeaderTarget`, `CommandSetRallyPoint`), and
  `CTaskManageArmy` appears only 4 times in `mercbrain`.
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

Their parameters are not known yet: a task's parameters are read in its `LoadFromXML`, not declared
in `RegisterProperties`, so each needs its own trace before a brain can use it.
