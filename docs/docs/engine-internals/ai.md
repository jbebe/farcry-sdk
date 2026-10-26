---
sidebar_position: 18
---

# AI

:::info[Verified via reverse engineering]
Traced through GhidraMCP against the symbolized `FarCry2_server`; the brain contents are read out of
the shipped `mercbrain.ai.rml`. The workspace loader, `GetChanceOfSuccess`, `ManageIntuition`,
`AdjustFOV` and the vision evaluators were matched in the retail PC `Dunia.dll` (Steam) and behave
the same. See [intro](../intro.md) for how
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

All of it is editable in JackAll's **AI** tab. Animals have their own agent, described in
[Animals](./animals.md).

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

The army-member states the lieutenant and the senses switch on are, by number: 0 social, 1 idle,
2 alert, 3 passive, 4 active, 5 defensive, 6 threshold, 7 dead, 8 briefing (read off
`CSensoryStimProcessor::ProcessStim`'s switch). "The combat states 3-5" on this page are passive,
active and defensive.

Two more lieutenant rules, **RE-verified**:

- **Camping.** Once a second it scores the player for staying put: +0.5 for moving less than 1 m,
  +0.4 under 3 m, +0.3 under 5 m (GOG `0x109225D0`, Steam `0x109335C0`). The score does not decay
  there. It lengthens the squad's memory: the lieutenant drops a target 10 s plus the score after it
  was last seen, so a player who stays put is hunted longer. The targets are a list on the army, each
  entry holding the target, where it was seen and when (`UpdateTargetList`, Steam `0x1093A2A0`, which
  prunes at most every 15 s).
- **Last man standing.** A member who is up and not in the threshold or dead state is sent
  `LastManStanding` when no other such member is within 200 m (`CheckLastManStanding`). The order
  only sets flag 42 (`CPawnAgent::LastManStanding`), and no plan in `mercbrain` tests flag 42, so the
  last man fights on as before.
- **Roles** balance suppressors against assaulters per range band (`IsNeededAsSuppress`); a member
  who suppresses has flag 9 set.

### Under fire

A bullet stim from an enemy reaches a soldier's `CSensoryStimProcessor::OnBulletShot` (Steam
`0x10A24770`) when it hits him, lands near him, or passes close to his head: every shot tests each
enemy pawn's distance from its line, against the weapon's `fShotJustMissedDistance` or 4 m. Each marks
him **under fire** with the shooter's position. A near miss also makes him flinch, at most once in 3 s
(`CPawnAgent::SetBulletJustMissed`, Steam `0x10960880`, which keeps the time at `+0xA94`).

In `mercbrain`, the ranged-attack plans react only to a bullet inside a capsule 1 m in radius and 2 m
tall around him: it stops his suppressive fire and starts the assault plan, which looks for cover
(flag 48). A bullet that misses by more changes nothing but the flinch. All of this is **traced in the
server build** and the brain data; the two Steam addresses were matched in `Dunia.dll`.

### Reinforcements

The reinforcement system calls in mercs and vehicles from `CReinforcementPoint`s placed in the
sectors. Each wave takes its archetypes from the point's own `MercNames` and `VehicleNames`
(`ExecutePendingReinforcementRequest`). The rules, **RE-verified on GOG**:

- **When a region may call.** A region without its own budget may call only while its counter is
  below 1 in world 1 and below 2 in world 2. It also needs fewer mercs than `iMercDensityThreshold`
  and density to spare (`0x10921B50`).
- **How many at once.** Fewer than 5 reinforcement vehicles and fewer than 20 mercs, pending and
  active together (`0x10919E40`).
- **How often.** A region waits 120 s between calls, 0.5 s for an instant region. After a failure
  it retries after 15 s, or 1 s for an instant region.
- **How far the point is.** 15 m to 181 m from the target, or 15 m to 50 m for an instant region.
  For some armies the minimum drops away, but the maximum stays.

`gamemodesconfig.xml`'s `ReinforcementArchetypes` feeds a separate path only: `GetReinforcementArchetype`
has one caller, `SpawnWildReinforcementScenario`, which `CPawnAgent::OnEvent` starts. Which event
starts it was not traced. Editing that list does not change what a point's waves bring.

## Garrisons and patrols

**A post refills when its sector reloads.** Soldiers do carry a `CPersistComponent`: every
`enemy_archetypes.Blue_Faction.*`, `Red_Faction.*` and `Special.*` archetype has `selLevel` 1, as do the
ghost patrols. But `CPersistenceDB::SaveEntity` saves a world-spawned entity only at level 2 and up,
or at level 1 when the persistence budget is −1. The shipped `PersistenceBudget` is 1000, so
soldiers are not saved.

What keeps a dead soldier gone for a while is the dynamic-entity limiter. A removed body is recorded
as deleted in its sector, and the sector's spawn list skips deleted entities when it loads. When a
sector's main layer unloads, `ForgetDeletedInSector` (GOG `0x1055A520`) wipes that sector's list,
and the next load spawns the full garrison again **(RE-verified)**. On the server a corpse is removed
only when its list is full; the 15 s age limit applies to the physics-body list alone. Which lists the
PC build enables was not traced, and whether a budget of −1 makes soldiers persist is untested.

**Ghost patrols are rebuilt each time.** Every `GhostPatrols.Patrols.*` archetype is
`bIsPersistent = False` with `fSpeed` 8. When such a patrol respawns, `CGhostManager` sends an event
that spawns a new vehicle, rather than restoring the old one. While ghosted, out of sight, the patrol
moves along its path at `fSpeed` × time (`CGhostEntity::GhostUpdate`). A blocked spawn point moves it
one more second along its path. Convoy and CopKiller ghosts are persistent and disabled. This is
**RE-verified on the server** and seen in data.

## Seeing

The archetype's `SensorySystem` gives a focus cone and a wider peripheral cone under three names -
`DesertFOV`, `SavannahFOV`, `JungleFOV`, each with `fLength` and `fAngle`, the full angle in degrees.
Only `DesertFOV` is ever read, in every kind of terrain: the target scan passes the sensory system's
first cone pair (`+0x88`), and the one cone getter returns that same pair with no region argument
(`0x109f3e50`, `0x109f1d50` in the GOG build, **RE-verified**). The other two are loaded and never used,
so editing `SavannahFOV` or `JungleFOV` changes nothing. `CSensorySystem::AdjustFOV` sets both cones
again for every target on every update:

- **Length** is `DesertFOV`'s `fLength` times the night term. In combat it is also times
  `fCombatMultiplier`, and when alerted before or after a fight times `fPreCombatMultiplier` or
  `fPostCombatMultiplier`; when idle there is no state multiplier. While he aims a sniper rifle it is
  times `fSniperLengthMultiplier`. Against the player in a vehicle it is times
  `fPlayerInVehicleMultiplier`, by day or with the vehicle's headlights on. Then it is capped: 150 m
  against the player, 350 m through a scope, less against other soldiers.
- **Angle** is `DesertFOV`'s `fAngle`, times `fSniperAngleMultiplier` through a scope. A soldier
  sitting in a vehicle sees all around him: both angles become 360°.

The **night term** is `1 − night × fNightTimeMultiplier`. *night* is 0 by day and 1 at night, and
ramps between them across dusk and dawn, four times kept by the dynamic environment manager. With
the shipped 0.5, cones are half as long at full night. This is the only thing darkness changes.
Against a target that fired a **muzzle flash in the last 3 seconds** the term is 1: firing at night
gives you away as if it were day.

Inside the cones, `VisibilityEvaluatorParameters` weigh how visible the player is from 0 to 1:
distance, cone edge, body coverage, occlusion, vegetation, stance and movement, each with its own
weight, plus grass, whose weight is not exposed. `fAmbientLightEvaluatorWeight` is loaded but never
read: there is no light evaluator, so no light in the scene, the sun included, reaches the AI.

Two of them depend on the cones (Steam, **RE-verified** against the server):

- **FOV** (`CFOVEvaluator::ComputeVisibility`, `0x10A21380`): 1 inside the focus cone's angle, then
  falling linearly to `1 − fFOVEvaluator_VisibilityFactorAtFOVLimit` (0.5) at the peripheral cone's
  edge, 0 outside it. A glimpse at the edge of the periphery can therefore still be a clear sighting.
- **Distance** (`CDistanceEvaluator::ComputeVisibility`, `0x10A20FA0`, a vtable slot the analysis did
  not make a function): it uses the **focus** cone's length whatever the angle.

**A soldier sees a target plainly only inside both cones.** Each vision update clears the target's
knowledge flags and sets one per cone the target is in; `GetVisibilityValue` without its
"remembered" switch needs both. With the shipped 60 m focus and 40 m peripheral cones, a player
40-60 m away straight ahead counts as not plainly seen.

For each candidate target, `AddPawnsToKB` resets the cones, lets `AdjustFOV` set them, then calls
`CSensorySystem::AddTargetDataToKB` (Steam `0x10A03120`, `RET 8`; `this` the sensory system, then the
visual target and the visibility context). The context there holds the final cones at `+0x04`, the
observer at `+0x08`, his eye at `+0x10`, the target's head at `+0x1C` and his view direction at
`+0x28`. The cone record keeps the archetype's own values beside the adjusted ones: focus length and
angle at `+0x08`/`+0x0C` adjusted and `+0x10`/`+0x14` as authored, peripheral at `+0x1C`/`+0x20` and
`+0x24`/`+0x28` (`SPawnFOVPerRegion::ResetFOV` copies the authored ones back). The knowledge entry
the call updates stores where the target was last perceived at `+0x28`, its head at `+0x34` and its
distance at `+0x40`. This is **traced in the server build** and matched in `Dunia.dll`.

In the range, stock and modded alike, a rifleman facing a player standing in the open 50 m away only
investigated, walked to where he had noticed him, and passed a player who had stepped 5-10 m aside.

**Seeing is not recognizing.** Visibility becomes a threat only in `CTaskFindVisualThreat` (Steam
`0x1099A050`, `RET 4`): it sorts every agent he sees into clear threats (above his "clear" value) and
faint ones (above "fuzzy") and succeeds once either list holds anyone, which sends a target-seen
report. It keeps polling while it finds nothing. The idle brain starts it once; every alert plan starts
it at its `OnStart`. So an alerted soldier whose search has just reported a faint sighting looks no
more until his next plan starts (**traced in the server build**, matched in `Dunia.dll`). In the range
no soldier was ever seen in that state: a mod that kept the search going never acted. The search
reads visibility through `GetVisibilityValue` without its "remembered" switch, so it counts only
targets plainly seen, inside both cones: a glimpse in the periphery alone is never even a faint
threat, and soldiers never notice anyone from the side. `SetVisibilityValues` also sets two more values per state (idle 10
and 50, alert 1 and 50, threshold, special and vehicle 1 and 100, combat untouched); what reads them was
not traced. The agent's `ThreatLevel` meter is fed only by Bang and Fear stims.

The evaluators combine as a product: each with value *e* and weight *w* contributes
`1 − (1 − e) × w` (`CVisibilityEvaluator::Evaluate`; GOG `0x10A0BFC0`). A weight of 0 skips the
evaluator. An evaluator that returns 0 makes the whole product 0, whatever its weight. Within that
product **(RE-verified on GOG and the server)**:

- **Distance.** Full visibility inside the first `fDistanceEvaluator_FullVisibilityRatio` of the
  cone's length, falling linearly to `fDistanceEvaluator_MinVisibilityAtMaxFOVRange` at its end, 0
  beyond. A target within 2 m is visible outright. A player with his back to a soldier who is not
  in or just out of combat counts as `1 − ramp(d, 5 m, 15 m)`: unseen beyond 15 m.
- **Stance.** Crouching (stance 1) is 1 up to 15 m, then falls linearly to 0.6 at max(35 m, ⅔ of
  the cone). Stance 2 and swimming are 0.6; stances 0, 3 and 4 are 1. A target at least 0.5 m under
  water is invisible.
- **Movement.** `1 − (1 − fSpeedEvaluator_StandingStillVisibilityFactor) × (1 − s) × ramp(d, 10 m,
  max(20 m, cone))`, where *s* is the target's speed against a reference speed, clamped to 0–1. So
  standing still only helps beyond 10 m.
- **Body coverage.** For a player on foot, `CPawnSamplingEvaluator` (Steam `0x10A21D90`, `RET 4`)
  casts three rays at points on his body and counts a third for each that gets through, scaled by
  the transparency of anything it passes. Walls count here; `COcclusionEvaluator` reads only AI
  occlusion volumes. In the range a player standing behind a 1.3 m barrier, or crouched behind a
  1.4 m wall, had half the visibility he had in the open.

### Grass and stealth

The grass evaluator is the player's camouflage. It needs a `CStealthComponent`, which only the player
archetypes carry, so it is 1 for anyone else. `CStealthComponent::Update` sets its flags only while
the player is crouched, samples the grass every 0.25 s, and counts him as hidden once he has been
crouched in high grass for more than a second without moving 0.2 m.

- **Concealment** is `1 − ramp(d, 3 m, 10 m) × k`. *k* is 0.9 for an idle observer, 0.765 for one
  alerted before a fight, and 0 in or just after combat. The observer's side of the player picks
  which grass sample counts, front, left or right. An observer looking down at more than 30° is not
  fooled.
- **Camouflage**: with a `stealth` bonus and the hidden flag, outside combat, the player is
  invisible beyond 2 m to an idle observer and beyond 4 m to an alerted one.

This is **RE-verified** (`CGrassEvaluator`, GOG `0x10A10DD0`).

### Hearing

Sound reaches soldiers as stims. Only the player's archetypes carry `Stim_FootSteps`: type Snap,
level 1, 7 m, with falloff. A weapon's muzzle stim is type Bang and falls off to level 1 at its
radius, as the entity data gives it:

| Weapon | Level, radius |
|---|---|
| AS50 | 12, 200 m |
| M249 mounted | 11, 85 m, no falloff |
| AK-47 | 8, 75 m |
| Makarov, MK19 mounted | 7, 50 m |
| MK19 carried | 10, 50 m |
| MAC-10 | 8, 3 m |
| MP5 | 3, 5 m |
| Dart Rifle, 6P9 | 2, 2.5 m |

The Carl Gustaf, the LPO-50, the crossbow and the DLC shotguns have no muzzle stim.

`CSensoryStimProcessor::ProcessStim` drops a soldier's own stims. It also drops his own army's,
unless he is in the social state or the stim comes from a vehicle's AI. An idle soldier who hears a
Bang files a threat report 0.2 s later at priority 7; a Snap files one at its own priority. All of
this is **RE-verified on the server** and seen in data.

### What a soldier remembers

A sensory record of a pawn lasts **120 s**. One of an object lasts 5 s, or 2 s for object types 3
and 4. A soldier also notices a target by touch, within a radius that depends on his state:

| State | Radius |
|---|---|
| idle | 1 m |
| alert | 2 m |
| passive, active, defensive | 3 m |
| threshold | 1.5 m |
| social, dead, briefing | none |

A dead pawn within 20 m is always taken in, whatever the soldier's focus. This is **RE-verified on
the server**.

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
  Every shot misses before then. The timer starts at his first shot in combat and is stopped only when
  he leaves the combat states (`UpdateShootingSystem`), so losing sight of his target and finding it
  again does not restart it.
- **Hit cap** - 0 once the target has taken `uiMaxHitPerSecondFactor` hits in the current second.
  The value read is the one in the *target* status block; the shooter's copy is unused. It is 5 in
  every archetype that sets it.

Inside `fPointBlankDistance`, once `fTimerToPointBlank` has run out, a soldier's chance is simply
1: every shot hits, with no timer to miss, no hit cap and no forced misses.

`GenerateShootAtPosition` then rolls **the average of three uniform random numbers** against the
chance. The average clusters around 0.5, so the roll exaggerates both ends: a chance of 0.7 hits
about 88 % of the time, 0.3 about 12 %.

**Forced misses.** After a hit, beyond point blank, the shooter misses the next *N* shots outright.
*N* is drawn between two fields of the weapon's `CWeaponPropertiesCommon`, per difficulty -
`nForcedFailureMin`/`nForcedFailureMax` × `Causal`, `Experimented`, `Hardcore`, `Infamous` - using the
game's difficulty when the target is the player and the normal values otherwise. The AK-47 ships
4-8, 2-4, 1-1 and 0-0: on Infamous nothing breaks up a run of hits. Others differ:

- **The same on every difficulty:** the MP5 is 3-5 throughout.
- **Falling with difficulty:** the RPG-7 is 4, 3, 2, then 1-2.
- **Never:** the AS50, USAS-12, 6P9, machete, MGL-140, M79, IED, Carl Gustaf, Dart Rifle, flare gun,
  LPO-50, carried M2, both MK19s, the mortar and the DLC weapons are all 0.

Shooters of special types 18-29 never start a streak.

The same properties name the weapon's range curve (`archTargetDistanceCurve`) and
`archSuccessfulHitCurve`. **Every weapon shares one range curve,
`Curves.ShootingSystem.DistanceAccuracy`**, except the mounted M249's two entries. The library also
defines per-weapon curves, `DistanceAccuracy_AK47` and 13 more, but no archetype names them, so editing
one changes nothing (seen in data).

Missed shots land in a `fMissWidth` × `fMissHeight` box around the target.

**Where a hit lands** is the aiming dot: a body part, head or torso, kept in `CShootingSystem` (`+0x1C`)
and set up by `SetupAimingDot` (Steam `0x10A193E0`). The brain asks for the head at short range.
After a hit the engine only zeroes the dot's offset and never rebuilds it, so a soldier who has hit
keeps aiming at the same part. In game, about two thirds of the soldiers' hits on the player landed on
the head; with hit locations off for the player ([the player](./stims-and-damage.md#the-player)),
stock never shows it.

**Whether he fires at all** is `CTaskShoot::CanShoot` (Steam `0x109792F0`). `CTaskShoot::Shoot` calls
`StartShooting` before it generates the shot's position, so the trigger is pulled before
`GenerateShootAtPosition` runs. `CanShoot` does not test how well he sees the target, so during the
[intuition](#intuition) window a soldier fires at a player he cannot see. How visible the target is to
him is `CSensorySystem::GetVisibilityValue` (Steam `0x10A021A0`), the smoothed value his knowledge base
keeps for that target.

`GetDifficultyFactor` and `GetProgressionFactor` are multiplied in but return 1 in both builds (the
PC build inlines them as constants). Difficulty reaches accuracy only through the forced misses.

### Where difficulty acts

The game has three dials, and none of them watches how the player plays: the **difficulty setting**,
the **progression level** (0-27, one per story or library mission) and the **act**. The player's
**reputation** rises with them. Of these, the AI sees:

| What | Dial |
|---|---|
| Accuracy | the setting: forced misses per weapon (`nForcedFailure…`) |
| Squad roles | the setting: `FindRoleDistribution`'s split, which no brain reads |
| Enemy weapons | progression ([enemy loadouts](./campaign.md#enemy-loadouts-follow-campaign-progress)) |
| Optional tactics | progression ([adaptive behaviours](#adaptive-behaviours)) |
| Weapon condition | the act ([soldiers](./stims-and-damage.md#soldiers)) |
| Going down instead of dying | the player's reputation ([soldiers](./stims-and-damage.md#soldiers)) |
| Everything else in this page | not at all - archetypes, cones, thresholds and behaviour odds are the same on every setting |

What else the setting changes is in [campaign](./campaign.md#what-the-difficulty-setting-changes).

### What sets one soldier apart

Little. Every world 1 enemy archetype - `Blue_Faction` and `Red_Faction` × assault, light machine
gunner, rocket man, shotgun man and sniper × `Caucasian` and `Nubian`, the two `Special.SpecOps_*` and
`Missions.Assassination_Target` - has the same shooting and sensing values: `fTimerToMissTarget` 0.2,
`fPointBlankDistance` 2.5, `fTimerToPointBlank` 1.5, `fMissWidth` 3, the same stance and speed factors,
`uiMaxHitPerSecond` 5, `fAgentHealth` −1 (300 for the assassination target), `WeaponJamProbabilityScale` 1
and `selAIInfamyMode` 2. What differs (seen in data):

- `fCombatMultiplier` is 4 for snipers, rocket men and the assassination target;
- `selODU`, the unit type, is 4 for snipers and 1 for SpecOps;
- `bHasALongRangeWeapon`, and the weapons themselves.

Three per-soldier values exist at run time, none of them used in combat (**traced in the server
build**):

- **His own reputation.** `selAIInfamyMode` 2 makes it random. The agent keeps a 0-1 fraction (`+0x610`,
  saved as `AIInfamyValue`) that places him between `AIInfamyRange`'s `lowAIInfamy` 15 and `highAIInfamy`
  30, above the current act's start level. `CPawnAgent::GetRelativeInfamy` compares it with the
  player's reputation, from much lower to much higher. `CTaskCheckRelativeInfamy` reads the comparison
  124 times in `mercbrain`: 108 in the social plans, 15 in idle, 1 in the emotion strategy. It makes
  him back away from or stand up to a player who aims at him in a ceasefire town.
- **His stress level** (`+0x614`, `AIStressLevel`). `CTaskCheckStressLevel` reads it only in the look
  and emotion strategies: where he looks and what face he makes.
- **His mental state**, set every update from his army-member state (`CPawnAgent::UpdateMentalState`),
  picks animation skills and bark banks.

**No soldier flees or surrenders in combat.** `CTaskFindSocialFleePos`, the only flee, runs in the
social and idle plans: aimed at, stared down, crowded. In combat, a soldier leaves a fight only through
the squad's `Fallback` order (flag 57), the [downed state](./stims-and-damage.md#soldiers) or death.
The opposite exists too: `RushTarget` (flag 43) and `CombatNoCoverRunToThreat` (flag 55) send him at
his target.

## Adaptive behaviours

Some optional tactics are rolled against a percentage. `CFCXGameplayManager::GetBehaviorChancePercentage`
looks the behaviour up in the `AdaptativeBehavior` table of `gamemodesconfig.xml` and reads the
column for the level the Weapons service reports - the same 0-27 progression level that selects the
enemy weapon packs, not the difficulty setting. The twelve shipped behaviours:

`Grenade`, `GrenadeAndBuilding`, `ChaseWithVehicle`, `ReachSniperWithVehicle`, `MountedWeapon`,
`ShootFlare`, `ShootInterestingObject`, `RescueVictim`, `RangeWeapon`, `VehicleChaseLevel2`,
`VehicleChaseLevel3`, `LongRangeVehicle`

**Only seven of them do anything.** The chance has one reader, `CTaskCheckDifficultyLevel`, which rolls
it and exits `DoIt` or `DontDoIt`; its `AdaptiveBehavior` index 12 always means `DontDoIt`. Two of the
14 shipped brains use the task:

| Row | Behaviour | Used by |
|---|---|---|
| 2 | `ChaseWithVehicle` | `mercbrain`, once: whether a soldier takes a vehicle after a target in one |
| 3 | `ReachSniperWithVehicle` | `mercbrain`, twice: escaping a mortar, and the alerted sniper |
| 4 | `MountedWeapon` | `mercbrain`, 8 times |
| 6 | `ShootInterestingObject` | `mercbrain`, 18 times |
| 9, 10 | `VehicleChaseLevel2`, `3` | `vehiclebrain` |
| 11 | `LongRangeVehicle` | `mercbrain`, 5 times |

Rows 0 `Grenade`, 1 `GrenadeAndBuilding`, 5 `ShootFlare`, 7 `RescueVictim` and 8 `RangeWeapon` have no
reader, so editing them changes nothing. Flares in particular are not gated by this table. This is
**RE-verified**: the getter's only caller in both builds, and every shipped brain unpacked.

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
