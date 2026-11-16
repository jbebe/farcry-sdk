---
sidebar_position: 6
---

# AI and Combat

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`, the symbolized Linux dedicated server (read only in Ghidra); every
address here is the server's unless marked Steam. Data read from the retail 1.03 `patch` and
`common` trees: the 14 brains in `scripts/game/newbrains` unpacked, world 1's entity library, every
`w1_*` and `w2_*` sector, the GOSM state machines, `movemgr.bin`, the world deploys and the bark
banks. **Nothing here was seen in game.** Quoted voice lines come from automatic transcription and
are approximate. The AI itself is described in [AI](../engine-internals/ai.md); this page lists
what that code and data hold but nothing runs.
:::

Single player only. Each item says what exists, what keeps it off, and what turning it on takes.
"Code only" means the classes and functions are in the binary but no shipped data reaches them;
"code + data" means brains, state machines or archetypes also exist; "complete" means it runs and
only a value keeps it small.

| Item | Exists as | Kept off by | Turning on takes |
|---|---|---|---|
| [Fire teams and leaders](#squads-and-roles) | code only | no caller | FCSE plugin and brain data |
| [Army strength](#army-strength) | live code, frozen value | one writer, a console command | FCSE plugin |
| [Medic and engineer](#soldier-classes-and-rescue) | complete for the medic | `selODU` 1 on three archetypes | data edit |
| [Morale](#morale) | state names only | no morale value, no sender | new code and clips |
| [Weapon failure](#weapon-failure) | gate and states | nothing writes the threshold | plugin, brain data, clips |
| [Guard shove](#guard-shove) | code + data | no brain uses the task | brain data |
| [Blind fire from cover](#blind-fire-from-cover) | code + clips | no brain turns it on | brain data, a channel write |
| [Sleeping guards](#sleep-and-routines) | complete | nothing | nothing |
| [Reinforcement rays](#reinforcements) | code + data | `bUseRayCheck` 0 | data edit per point |
| [Stims nobody sends](#stims-and-events-nobody-sends) | handlers | no sender | emitter data |
| [Voice lines never requested](#voice-lines-never-requested) | complete banks | no request | brain or script |

## Squads and roles

`CDispatcherSquadLieutenant::DistributeSquads` (server `0x086A30D0`) and
`DistributeSquadRoleAndAction` (server `0x086904C0`) have no caller. **Code only.**

- **`DistributeSquads`** splits an army into up to 10 fire teams of 2 or 3 (random once four or more
  are free) from members within 20 m of one another. The first member is the team's leader (team
  slot `+0x3F`). A team whose leader is dead or unloaded is dissolved and its members are
  redistributed; a team of one merges back.
- **`DistributeSquadRoleAndAction`** walks the 10-team table (stride `0x50`) in three passes. It
  counts members under fire and members still waiting, and files each member into a per-target
  bucket. A member of no team is sent role report 1, `SquadRole` rogue (2) and `SquadAction`
  waiting (0). A member of a team is sent `SquadRole` leader (1) or follower (0) and a
  `SquadAction` computed from its current action (agent `+0x8D8`: 0 waiting, 1 waiting to move,
  2 moving, 3 attacking) and from whether it is in the team's "moving" or "covering" list. That is
  a bounding leap-frog: one half moves while the other covers. The first member whose action
  changed also gets `SquadImmediateAction`.
- **What the calls write** is two agent fields (`+0x8D8` action, `+0x92C` role) and the events
  `EVENT_SQUADROLE_*` and `EVENT_SQUADACTION_*`. The only readers are the unused
  `CTaskCheckSquadRole`, `CTaskCheckSquadAction` and `CTaskCheckArmyRoleAction`. The `SetSquadRole`,
  `SetSquadAction` and `SquadImmediateAction` orders are therefore never sent in the shipped game,
  and neither is `SetLeaderTarget` (`SendArmyMemberLeaderTargetReport` has no caller at all).
- **The team's goal** (the vec3 at team `+0x3C`) is written by nothing in these functions.

The brain-side classes exist, and no plan branches on a role or action. Turning it on takes three
things together: an FCSE plugin that calls `DistributeSquads` and then
`DistributeSquadRoleAndAction` from the lieutenant's update; `mercbrain` plans that branch on
`CTaskCheckSquadRole` and `CTaskCheckSquadAction` (advance when moving, suppress when attacking or
waiting); and a writer for the team's goal. A soldier's assault or suppress role
([Squads](../engine-internals/ai.md#squads)) is a separate, live mechanism.

## Army strength

`CFCXCountersComponentAI::ApplyArmyPillar` gives every soldier of the red or blue army that army's
health and weapon reliability. The ratings live in `CounterHealthRatingsTable` and
`CounterReliabilityRatingsTable`, four rows per act. **Live code, frozen value.**

- `CFCXCountersService::ResetPillars` (server `0x087A7460`, called once from `DoInit`) sets both
  armies to `GetMaxValueFromRating(3, act, counter)`, the top rating: 250 health in game.
- The only other writer is `SetPillarValue` (server `0x087A7400`), reached only by the console
  command `cheat_set_pillar` (`ConsoleCommandCallback`, server `0x087AB550`).
- So the medium, low and failure rows of both tables are never used, and an army never weakens
  during play.

Turning it on takes an FCSE plugin that calls `SetPillarValue(army, counter, value)` on game events.
The four ratings per act are already data. What the rating was meant to follow, kills or raided
supplies, is not stated anywhere in the code.

## Soldier classes and rescue

Every soldier archetype carries `selODU`, the unit type read by `CTaskCheckODUType` (server
`0x0840B6B0`): 0 Grunt, 1 Medic, 2 Boss, 3 Engineer, 4 Sniper. The task is used by no brain.

In world 1's library no archetype is Engineer or Boss, 4 are Sniper, and 141 are Grunt. Medic is
set on `Special.SpecOps_Assault`, `SpecOps_Shotgun` (A1LM04) and Doctor Obua.

A downed soldier is rescued by a comrade in `mercbrain`: the rescuer walks to him, attaches, carries
him to cover, drops him and heals him (`CBrainMercThresholdHealthRescuer` and `...Victim`, the
`pawn_hmrhealth_*` clips). Any comrade can do it, and the lieutenant's `ManageRescue` drives it.
**What the class changes** is in `CBrainMercThreshold::CleanUpThresholdStuff` (server `0x083C7FC0`):

- When a rescue ends, a rescuer whose `selODU` is 1 calls `CCounter::Fill` (server `0x09107C90`) on
  the victim's health counter, a full refill to that counter's maximum, and clears
  `HealthFailureWhileHealing`. A rescuer whose `selODU` is 3 does the same to the reliability
  counter.
- Any other rescuer runs the same plan with the restore skipped.

**Complete for the medic**, so setting `selODU` 1 on more soldier archetypes is a data edit. The
engineer half needs a writer for the reliability threshold ([Weapon failure](#weapon-failure)).
`CTaskFindRescueDest`, `CTaskGetRescuePositions`, `CTaskCheckRescueState` and `CBrainRescueBuddy`
are unused plumbing for a player rescued by a buddy, which `CBuddyRescue` does instead. The
`RescueVictim` row of the adaptive table has no reader
([Adaptive behaviours](../engine-internals/ai.md#adaptive-behaviours)).

## Morale

The AI has three pillars: health, reliability and morale (`hmr.gosm.xml` is Health, Morale,
Reliability). **Only the names exist for morale.**

- `EVENT_MORAL_THRESHOLD_CROSSED` is defined and read by nothing.
- `CFCXCountersService::SetPillarValue` and `GetPillarValue` accept only counters 0 and 1, health
  and reliability. There is no morale counter.
- The blackboard fact `RescueMoralBoost` is only ever removed, in `CleanUpThresholdStuff`.
- `hmr.gosm.xml` holds `::HMR/HMR/Morale/*`: `Victim_MoralFailureStart`, `Victim_MoraleSpray`,
  `Victim_MoraleFailure` (shoot and reload loops), `Victim_MoraleStun`, and for a boss
  `Boss_HolsterWeapon` leading to `Boss_MoraleFailure_Rescue` (signal `MoraleRescue`) or
  `Boss_HolsterWeaponExecution` leading to `Boss_MoraleFailure_Execution` (signal
  `MoraleExecution`). Every state has `animStateID` 0, so no clip is assigned; only the holster
  layer is set. No brain sends `MoraleFailure`, `MoraleRescue` or `MoraleExecution`.
- No archetype is `selODU` 2 (Boss).
- The Morale stim (type 11) is routed to `CAIEngine::OnEvent`, and no AI state handler has a case
  for it.

What the state machine describes is a soldier who breaks and sprays fire, and a boss who talks him
back or shoots him. No surrender or weapon-drop state exists anywhere. Turning it on takes new
code for the morale value and its trigger, new clips, and brain plans.

Elsewhere in combat the engine has no flee: `TryFlee` is off, `CTaskFindSocialFleePos` serves only
the social and idle plans, and the last man standing only plays a bark
([Squads](../engine-internals/ai.md#squads)).

## Weapon failure

A downed state for a failing weapon is half built. **Gate and states, no writer.**

- `CBrainMercThreshold::DoStart` (server `0x083C6550`) dispatches the filter
  `ThresholdPlayReliabilityVictim` when the agent's `PillarThresholdCross` (`+0x624`) is 1.
  `mercbrain` has no selectable of that name. It is the only filter among the brains checked
  (combat, alert, special, threshold) that the code can select and no brain answers.
- The one live writer, `CPawnAgent::OnEvent` (call at server `0x085B382B`), calls
  `SetPillarThresholdCross(0)` only for the health counter. Nothing writes 1, so
  `CTaskCheckPillarThreshold` would never see `EVENT_RELIABILITY_THRESHOLD_CROSSED` either.
- `hmr.gosm.xml` `Reliability/*`: `Victim_ReliabilityStart`, `DrawWeapon`, `Shoot`, `Failure`,
  `Stun`, and `Explose` (which ends in a ragdoll), then `Mechanic_Holster_Weapon` leading to
  `Mechanic_Restore` (signal `ReliabilityRestore`). The signals `ReliabilityFailurePrimary`,
  `...Secondary`, `...NoWeapon`, `ReliabilityRestore` and `ReliaibilityExplose` have no sender.
  Every state has `animStateID` 0 apart from the holster and aim layers.
- Clips that exist: MOVE has `Pawn_Generic_Jam`, `Pawn_Generic_UnJamTry`,
  `Pawn_Generic_UnJamSuccess` and the `FixWeapon` smart-terrain clips, live as an ambient duty with
  114 placed.
- `CGameConfig`'s `UnlimitedReliability` is a different switch, on the profile
  ([Settings and Scripting](./settings-and-scripting.md)).

Live values that shape jams: `WeaponJamProbabilityScale` is 1 on 209 counters components and
multiplies a weapon's jam probability in `CFCXWeaponsService::GetWeaponJamProbability` (server
`0x0879FCE0`); 0 means no jams for that owner. `fFakeReliability` is 1 on the 14 weapons AI use,
which fixes their reliability at full (reader not traced).

Turning it on takes a plugin that writes threshold 1, a `ThresholdPlayReliabilityVictim` plan in
`mercbrain`, and animation for the states. The engineer's restore then works as described above.

## Reactions and gestures

### Guard shove

`mercbrain` sends `shove_player` 48 times, and `social.gosm.xml` plays `pawn_gesture_shove` and the
`start_shoved` and `start_charged` reactions. That is an arm gesture with no effect on the player.
**The physical version is unused.**

- `CTaskPushPlayer` (constructor server `0x083670E0`, `StunPlayer`) with `CPawnPushPlayerEvent` in
  three push types. `CFCXAIBehaviorService::GetPushImpulse` (server `0x087D10E0`) reads
  `LightPushImpulse` 3, `MediumPushImpulse` 4, `HeavyPushImpulse` 6 and `InterpSpeed` 4 from
  `gamemodesconfig.xml`, and the event handler at server `0x085B2A90` applies them. The task and
  `LightPushImpulse` are in the Steam `Dunia.dll` as well.
- `specialmercbehaviors.gosm.xml` has a `PushPlayer` state that plays `Pawn_Sync_Gesture_PushAway`
  on the `PushPlayer` signal. Its CRC `0x5194740B` is not in `movemgr.bin`, so the full-body clip
  is probably missing.
- None of the 14 retail brains instantiates `CTaskPushPlayer`.

Turning it on takes a `CTaskPushPlayer` plan in the social branch of `mercbrain`, plus a MOVE state
for the push, or a plugin that applies the impulse on the existing gesture.

### Blind fire from cover

`CTaskCoverAttack` has a `Blindfire` attribute (`LoadFromXML` server `0x08313370`, field `+0x3C`).
Only `merctestbrain` uses the task, with `Blindfire` 0, and no soldier brain does. The shipped
blind fire is the live `CombatBlindly` plan. Three finished AK-47 clips exist
(`3rdge_fulb_crouch2blind_...`, `...blindshoot01_...`, `...blind2crouch_...`), each alone in an
unnamed layered state. The `CoverStance` channel (None, LeaningLeft, LeaningRight, LeaningUp,
Blindfiring) has all four non-default branches playing `invalidanim.mab`. The server has no string
for the channel, so what writes it is not in the server build.

### Hands up and panic

`pawn_gesture_handsup1`, `handsup2` (and `gun` variants), `panicv1`, `panicv2`, `lookaroundpanic`
and `lostpanic` are loaded in world 1's deploy and named by CRC from the bark banks
(`scripts/game/barkdata/*.bank`), so they play as bark gestures. No brain or GOSM state names them.
The aimed-at reaction `pawn_social_aimedat_start`, `loop`, `loop2`, `end` is a second set: hands up,
high or low by the player's infamy, with or without a gun. The shipped `AimedAt` plans play
`Pawn_Gest_IntimidationStand` instead, and the unnamed set adds the unarmed hands-up
(`3rdge_gest_handsup1/2`).

### Machete takedowns and other stubs

`Pawn_Sync_Machete_BeginKill`, `FinishDefault`, `FinishDisarm`, `FinishDouble`, `FinishFlip`,
`FinishGrenade`, `FinishPush` and `FinishSlam` are MOVE states whose clip is `invalidanim`. The
live machete states are `Scare`, `Miss`, the ground finish and the double-kill event. A set of
gestures is also unanimated (clip `invalidanim` or an unshipped hash): `Pawn_Gesture_PutOutFlames`,
`pawn_generic_BurnOnGround`, `Pawn_Gesture_Knock`, `pawn_gesture_shove1left`, `shove1right`,
`Pawn_Gesture_BeckonBig`, `pawn_gesture_QuestionAlert`, `QuestionRelaxed`,
`pawn_gesture_SearchTargetLow`, `pawn_gesture_losttargetb`, `pawn_gesture_fingercounting`,
`pawn_gesture_firingpistol` and `Pawn_Generic_BulletReaction`. Restoring any of them takes new
animation.

### Two finished clips with no state

- `Transition_IdleCombat2Alert` (`3rdge_fulb_idlecombat2alert_...`) is a combat-to-alert
  de-escalation; its pair `Transition_IdleAlert2Combat` points to an unshipped clip.
- `pawn_buddydown_cradleentryshort` is a shorter entry to the buddy-down syrette scene; the live
  scene uses the long one.

### Hurt lean

A soldier hit by any damaging stim leans procedurally: pitch -9 to -4 degrees, yaw plus or minus 9,
roll plus or minus 12, in at 9 per second and out at 8 (`CProceduralRecoil::UpdateProceduralHurt`,
server `0x09019CE0`). The values are archetype properties (`hurtleanPitchMin` to `hurtSpeedOut`),
and the two archetypes that set them use the defaults. It is live; larger angles are a data edit.

### Speed variation

`CGameAgent::ConvertSpeedWithVariation` (server `0x0856C8F0`) returns the gait's base speed plus
`v * (2r - 1)`, where `v` is the gait's `fVariation<gait>` in metres per second and `r` a uniform
random number in 0 to 1. `fVariationBabyStep`, `fVariationJog` and `fVariationSprint` are 0 on all
337 agent archetypes; `fVariationWalk` is 0.01 to 0.2 on about 33 and `fVariationRun` 0.1 on 12. So
a squad sprinting on the same order moves at one speed. A data edit changes it; who calls the
function per order was not traced.

## Sleep and routines

Idle soldiers choose smart-terrain points by time of day, and some sleep. **Complete.**

- 35 `Rest.SleepOnBed` and 98 `Rest.SleepOnGround` points are placed in single-player sectors. Idle
  soldiers pick duty, rest or social points from a table by hour
  (`CFCXAIBehaviorService::GetSTPProbabilitySet`; rest is 70 % from midnight to sunrise). STP
  `selNeedType` 0 Rest, 1 Duty, 2 Social, with `fCoefficient` weights, is read by
  `CTaskSmartTerrainFinder::FindSTPForNeed`. There are no need meters such as hunger or fatigue.
- Only the two sleep points have `bIsUnawareSTP` true.
  `CTaskSmartTerrainExecutor::SetupSmartTerrain` copies it to agent flag 60 and `Cleanup` clears
  it. `CSensorySystem::UpdateVision` (server `0x08705110`) does nothing when flag 60 is set: no
  visible objects, no pawns, and the knowledge base does not tick or expire. A sleeper therefore
  cannot wake by sight. Hearing and stims are separate and were not traced.
- `CTaskCheckRegionTransition` fires on a change between discretionary and free-fire regions (agent
  `+0x924`, `+0x928`, `UpdateRegionTransition`). No brain uses it.

Smart-terrain archetypes with no placement in any sector:

| Group | Archetypes | Clips |
|---|---|---|
| Placeable now | `Rest.LeanBackToWallSocial`, `LeanHorizontalHighSocial`, `SitOnBenchSocial`, `SitOnChairSocial`, `SitOnGroundSocial`, `SitOnTableEdgeSocial`, `Social.SocialSitAtTable` | states retail points already use |
| Placeable, wrong animation | `Duty.LightTorch_PREFAB`, `ExtinguishTorch_PREFAB` | play the tyre-kick clips |
| Need clips | `Duty.BodyGuard`, `CleanVerticalSurface`, `WipeBar`; `Rest.LeanOnTable(+Social)`, `LeanVerticalLeft/RightShoulder(+Social)`, `SpectatorA`; `Social.Spectator`, `SpectatorB`, `DutyAskDirections`, `RestAskDirections`, `KitchenPatrolB` | clip hashes absent from the retail `.fat` family |

Placing one is sector data. Prefab placements were not scanned, so a few may be reached through
prefabs. Other cut smart-terrain actions are `Pawn_ST_Rest_Jumpjack`, `Pawn_ST_Duty_RefuelVehicle1`,
`pawn_st_social_potatob`, `Pawn_ST_Social_AbortedConversation` and the `Pawn_ST_Abort_*` set.

## Reinforcements

The flare, the points and the limits are in [Reinforcements](../engine-internals/ai.md#reinforcements).
What the code adds:

- **What starts the wild scenario.** `CReinforcementSystem::WildReinforcement` (server
  `0x08623C30`) has one caller, inside `CPawnAgent::OnEvent` (call at server `0x085BA3E0`). The
  branch is reached by an event of class `CFlareExplosionEvent`: a flare exploding, whether fired
  through `ShootFlare` or any flare grenade, makes each agent that passes a virtual test
  (`vfunc +0xC0`, meaning not identified) ask for a vehicle reinforcement at the flare.
- **Behind the player only.** `CReinforcementPoint.bUseRayCheck` is 0 in every constructor and never
  set in data. In `CheckReinforcementPointValidity` (server `0x086A9790`) a point must be at least
  15 m from the target (squared 225), and without the ray check it is valid only if it lies 90
  degrees or more behind the target's facing. With the flag a point in front is accepted when a
  delayed ray from the target's head height finds it hidden, outside the `SpawnExclusionList`. A
  data edit per point.
- **No radio.** There is no camp-to-camp messaging: no strings or classes, and `CRadio` is a sound
  object. Each army's lieutenant acts alone.

## Stims and events nobody sends

Stim types are 0 None, 1 Bang, 2 Snap, 3 Pierce, 4 Crush, 5 Cut, 6 Health, 7 Burn, 8 Water,
9 Panic, 10 Fear, 11 Morale, 12 Dirt, 13 Reliability, 14 Interest. Data carries only types 1, 2, 3,
4, 5, 7, 9, 10 and 12, and code constructs only 1, 3, 4, 5, 7 and 10. Types 6, 8, 11, 13 and 14 are
handled and never sent.

| Stim | Handler | Effect if sent | Turning on |
|---|---|---|---|
| 6 Health | `CFCXCountersComponentAI::HandleStim` (server `0x087EEC20`) to `RestoreHealth` (`0x087EEA80`) | heals any soldier or animal with a counters component in radius, through the `OldRestoreHealth` curve | a `CStimsEmitterComponent` with a Health stim in an archetype (data) |
| 13 Reliability | same function to `RestoreReliability` (`0x087EC560`) | repairs an AI weapon's reliability, curve `OldRestoreReliability` | same, data |
| 14 Interest, detail `BatsEffect` | the five non-social state handlers call `CPawnAgent::ComputeBatsEffet` (server `0x08590190`) | a soldier swats at bats: `MercBrainSpecial/BatSwat` in `mercbrain`, three intensities, 30 s cool-down | stim whose `sDetail` is the hash of `BatsEffect`, plus animation for `batswat_light`, `_medium`, `_heavy` (not checked) |
| 8 Water, 11 Morale | none | none | new code |

The Health stim's effect on the player's counters was not traced (`HandleDamageStim` tests only
type 7). The `NPCDefault` stim table has an empty `Water` entry and `Morale` pointing at
`OldRestoreMorale`. The bat chain is complete in code and data on the soldier side. Its trigger is
the special brain `SpecialBrainBat`, chosen by a blackboard value; the sibling special brains for
burn, drown and fall to death exist the same way.

**Herd panic never reaches soldiers.** The relay in `CoreRun` (server `0x0855D770`) iterates
`CAnimalAgent`s only and sends the panic stim to them every 0.25 s
([Animals](../engine-internals/animals.md#the-herd-panic)). `ProcessStimIdle` (server `0x08736E20`)
has a Panic case, but it serves gunshot-detail stims. No brain source mentions panic. A plugin that
forwarded the animals' stim to nearby `CPawnAgent`s would reach code that already exists.

**Infamy events.** `Stimuli_PlayerInfamyApproachingRedline`, `InRedline` and `Spiking` are
registered at `DoInit` (server `0x087BF5D0`) and read by nothing else; the Steam build has the
matching `EBarkEvent_PlayerInfamy*` names, referenced only by the enum's name table. Nothing raises
them. `CTaskCheckPlayerInfamy` (server `0x08410DA0`) exits `HighInfamy` or `LowInfamy` from
`CFCXCountersComponentPlayerSP::IsInHighInfamy` and no brain uses it, whereas
`CTaskCheckRelativeInfamy` is live. The music database's `sndHighInfamySoundCue` is `0xFFFFFFFF` in
retail `music.xml`, so no high-infamy cue plays. The mix slot `Infamy_FX` (22) has no code reader.
Turning the feature on takes a cue with a sound id and type 22 and a raiser for the three events.

**Test events.** `CEventCommonReportEventTest`, `CEventMercReportEventTest`,
`CEventCommonCommandTest`, `CEventMercCommandForceBehavior` and `CTaskDebugSetCurrentBehavior` have
no constructor caller. `ForceBehavior` would put a merc brain into a named behaviour. These are
scaffolding; their handlers were not read.

## Voice lines never requested

Six bark tags have banks and lines, and nothing asks for them: the tag CRCs are in neither binary
and no script passes the strings. Every line resolves to a record with audio.

| Tag | Lines | What the audio says |
|---|---|---|
| `BBX` | 12 | a buddy's last words before the player executes him: "Just do it.", "What are you waiting for?", "You win, asshole." |
| `MOBILE` | 79 | one side of a phone call: bets on a match, a diamond lifted from a doctor, "Can I call you later?" |
| `IIW` | 30 | witnesses to a killing: "We gotta help him!", "who is this sick fuck?" |
| `IMA` | 40 | a massacre or machete rampage: "My God, he's hacking everyone!" plus pained cries |
| `SOL1` | 23 | soldiers hunting a hidden player: "We got him cornered!", "I lost contact." |
| `SOL2` | 31 | taunts: "you gonna hide in there forever?", "I've got all day!", and laughs |

The `MOBILE`, `IIW`, `IMA`, `SOL1` and `SOL2` banks load with their infamy pair. Using one takes a
brain task or script `PlayBark` with the tag. The subtitles of `BBX` match its audio; what each tag
was meant to trigger is read from the lines.

## Other switches and unused classes

- **`mercbrain` switches** ([Switched off in mercbrain](../engine-internals/ai.md#switched-off-in-mercbrain)):
  `IsGrenadeAllowed` and `IsGrenadeAllowed1` default to 1 and are set to 0 only in phase 3 of the
  church `DefenceReversal` mission. `CheckPlayDisorganizedBark` is on in the unknown-threat search
  and off after `SawCorpse`. `SetFireStrategy` and `SetFireStrategy1` are off when escaping a
  friendly vehicle in idle and alert and on in combat. `IsDominoSTP*` is off in 6 places.
  `BackOffOnly` is off in 34 and on in 14; `KeepChestLookat` is off in 36 and on in 12. The
  `CBlueprintDecision1` inside each of the 9 `FleeFromLocation_BP1` copies is off.
- **Brains only test archetypes use:** `merctestbrain` (9 `MercTest.*` archetypes) and
  `stoopidsoldierbrain` (`MercTest.StoopidFollower`), none placed in a single-player sector.
  `missionbriefingbrain` is a 70-byte stub.
- **Other unused tasks:** `CTaskVehicleTurnAround` and the lists under
  [Implemented but unused](../engine-internals/ai.md#implemented-but-unused).
- **The behaviour service's variables** are read in `LoadFromXML_Legacy` (server `0x087D1C10`), and
  the 52 value nodes in the retail `gamemodesconfig.xml` block match it exactly: none is unread and
  none is missing.
- **Agent flags set by code.** Flag 60 is the sleep gate above. Flag 35 is set by
  `CPawnAgent::UnderWaterCheck`, so a soldier under water has it written by the engine.
- **Dropped weapons and gadgets** vanish after `dynamicLifeTime` seconds
  ([Settings and Scripting](./settings-and-scripting.md)); the same page lists the
  game-mode switches around vehicles and pickups.
- **Searched and absent:** trampled-grass tracking (the player's `Stim_FootSteps` is the only trace
  a soldier gets), a surrender state, needs meters, inter-camp radio, predators.

## Uncertain

- The values 3 and `0x18` that every squad report also carries in its role-action pair (not
  resolved to enum names).
- Whether a `SquadAction` of moving or attacking would change play, since no brain reads it.
- Which counter sits at `+0x30` and `+0x50` of the counters component (health and reliability by
  the names in `ApplyArmyPillar`; the layout was not read).
- The predicate at `CPawnAgent` vfunc `+0xC0` that gates the flare reinforcement, and the timer
  global compared there.
- Whether anything outside the server writes `CoverStance`, `IsMacheteKillReady`, `IsPlayerSleeping`
  or the `doMachete*` channels: they are not strings in the server, so any writer is in
  `Dunia.dll`, which was not traced for them.
- Whether any world places bats on soldiers.
- Brain filter comparison covers the combat, alert, special and threshold brains; the buddy,
  special-character, dead, social, vehicle and animal brains were not compared against their C++
  filters.
