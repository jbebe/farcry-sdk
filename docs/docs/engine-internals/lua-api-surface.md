---
sidebar_position: 5
---

# Dunia.dll — The Lua-Exposed API Surface

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification
(including the embedded Lua 4.1 (alpha) interpreter confirmation).

## Confirmed behavior: the Lua-exposed API surface

Every C++ function/method exposed to the embedded Lua interpreter goes through one choke point:
`RegisterLuaBinding(namespace_or_0, "Name", handlerPtr)` (`0x102aa850`, renamed from `FUN_102aa850`).
`namespace_or_0 == 0` registers a **global** Lua function callable from anywhere; a class-name string
(e.g. `"CBuddiesManager"`) registers a **method scoped to that manager's Lua handle**, typically
obtained via a `GetXxxManager()` global first (several of which are themselves in the global list
below, e.g. `GetBuddiesManager`, `GetFCXMissionManager`).

Surveyed **every one of the ~260 call sites** (`get_xrefs_to` on `0x102aa850`, fully enumerated — no
more exist beyond what's listed here) across ~30 distinct registration functions, each corresponding
to one exposed class or global batch. This is now the authoritative map of the engine's scripting
surface; source for `SCRIPTS\MissionTools.lua` and friends.

### Global functions (namespace = 0, callable from anywhere)

**Entity/utility** (`0x10591120`, 22 fns): `GetInvalidEntityId`, `GetLocalPlayerId`, `GetEntityName`,
`IsEntityLoaded`, `IsEntityValid`, `AttachAnchor`, `ValidateSyncAnim`, `GetEntityInPrefab`,
`RemoveEntity`, `GetStringID`, `GetNoCaseStringID`, `PathID`, `SetPlayerActionMap`,
`PushPlayerActionMap`, `PopPlayerActionMap`, `SendForceState`, `DrawTextToScreen`, `SetVisibility`,
one unnamed-string-literal binding, `UnBind`, `GetGlobalString`, `GetGlobalNumber`,
`SpawnEntityFromArchetype`.

**Gameplay/mission scripting, batch 1** (`0x109fc830`, 70 fns — the single largest registration site,
almost certainly backing `SCRIPTS\MissionTools.lua`): `SpawnPickupMissionItem`, `AddWeapon`,
`AddGadget`, `CameraShakeAndGamePadRumble`, `SpawnWeapon`, `RefillWeaponAmmo`, `RefillSyrettes`,
`RemoveAllWeapon`, `RemoveAllGadget`, `RemoveAllInventory`, `SelectWeapon`, `DrawWeapon`,
`HolsterWeapon`, `TeleportEntity`, `SpawnReinforcementScenario`, `SetLookAtTargetID`,
`PlayAnimSimpleObject`, `PhoneCall`, `CanAccessMedicine`, `SendAchievementData`,
`RemoveScriptCallback`, `ClearScriptCallbackID`, `GetMalariaPillCount`, `SetMalariaPillCount`,
`StopMalariaBlackout`, `SetSicknessLevel`, `SetBaseInfamy`, `EnablePostFx`, `DisablePostFx`,
`SwitchCamera`, `SetToScriptedCollisionGroup`, `SendPierceStimWithOrigin`, `SendPierceStimNoOrigin`,
`LookAtTarget`, `StopLookatTarget`, `EnableWagerRegion`, `DisableWagerRegion`, `PlayEmotion`,
`PopUpEndOfGame`, `PopUpInGameCredits`, `StartDefenceReversal`, `StopDefenceReversal`,
`PopUpObjective`, `StartBargeAssault`, `StopBargeAssault`, `BargeBreakdown`, `BargeRepaired`,
`BargeEnableZone`, `BargeDisableZone`, `StartBuddyBetrayal`, `BindToVehicleRide`,
`UnbindFromVehicleRide`, `StartAnimalFollowPath`, `StopAnimalFollowPath`, `SetupVehicleFollowPath`,
`StartVehicleFollowPath`, `StopVehicleFollowPath`, `EnableScriptedAIMode`, `DisableScriptedAIMode`,
`ForceSocialRegionToCombat`, `EnableRadioBroadcast`, `StartTownEscape`, `StopTownEscape`,
`StartDentalPlan`, `StopDentalPlan`, `StartJacksBuddy`, `StopJacksBuddy`,
`GetClosestAutomaticPrefab`, `SetupDefenceRevesalMikesPlace`, `SetupBuddyHealth`, `EnableCheck`.

**Gameplay/mission scripting, batch 2** (`0x10725120`, 33 fns): `AddScriptedSceneParticipant`,
`RemoveScriptedSceneParticipant`, `GetFCXMissionManager`, `GetBuddiesManager`, `GetGameplayManager`,
`GetFCXBarkManagerService`, `GetWeaponBazaar`, `EnableTutorial`, `SetScriptedDeath`,
`StartDefoliantFX`, `GameChangeWorld`, `GameChangeWorldDefaultSpawnPoint`,
`GetTriggerComponentContacts`, `SendPlayGame`, `KillPawnAgent`,
`ChangeReinforcementRegionSpawnRate`, `OverrideMapTexture`, `GameStartStopAMBXScript`,
`ChangeHealingPreference`, `StartDesertStorm`, `StopDesertStorm`, `SetUsableEntitySize`,
`AddDiamonds`, `SetObjectiveState`, `AddOverrideDesertSetting`, `RemoveOverrideDesertSetting`,
`AttachAnimationPackage`, `DetachAnimationPackage`, `DeleteEntityInTrigger`,
`IsPartnerMissionUnlock`, `SendSomeoneTalkedEventToPlayer`, `BlockBuddyRescue`,
`UnblockBuddyRescue`, `SetCinematicUIMode`.

**Sound** (`0x10629290`, 2 fns): `StartSoundMixingFromLua`, `StopSoundMixingFromLua`.

### Class-scoped methods (manager singletons, obtained via a `GetXxx()`/instance accessor)

| Class | Method count | Methods |
|---|---|---|
| `CBuddiesManager` | 34 | `IsBuddyUnlocked`, `GetBuddyState`, `GetBuddyGender`, `IsBuddyAvailable`, `AssignBuddy`, `SpawnPrimaryBuddy`, `SpawnBuddy`, `GetBuddyList`, `GetBetrayedBuddyList`, `PositionSpawnedBuddy`, `RemoveBuddyFromWorld`, `MercyKilledBuddy`, `BetrayedBuddy`, `GetRescueBuddyEntityId`, `AddHistoryPointsToPrimaryBuddy`, `AddHistoryPointsToSidequestBuddy`, `SetSidequestMissionState`, `AddRescueBuddySpawnEventListener`, `RemoveRescueBuddySpawnEventListener`, `AddRescueBuddyChangedEventListener`, `RemoveRescueBuddyChangedEventListener`, `GetPrimaryBuddyName`, `CheatSetRescueBuddy`, `SetBuddyRescueActive`, `IsRescueBuddyAvailable`, `SetShowRescueBuddyInMenu`, `AddDefenceReversalEventListener`, `RemoveDefenceReversalEventListener`, `SetDefenceRevesalBetrayedBuddies`, `BypassSetPrimaryBuddy`, `BypassSetSecondaryBuddy`, `BypassSetBuddyUnlock`, `BypassSetBuddyHistoryPts`, `BypassSetBuddyLifeState` |
| `CFCXMissionManager` | 22 | `ActivateBuddyMission`, `SelectMission`, `GetMissionFaction`, `SetCurrentMission`, `MissionCompleted`, `GiveMissionReward`, `WasFactionBetrayed`, `BypassMissionCompleted`, `BypassLibraryMissionOffer`, `BypassSetLibraryMissionState`, `SetLibraryMissionState`, `HeardBriefing`, `SetGreetingHeard`, `GetGreeting`, `GetAcceptedMissionCount`, `GetSubvertedMissionCount`, `SetWinningFaction`, `GetWinningFactionPerAct`, `GetWinningFaction`, `AddMovieSequenceToUnload`, `NewJackalTapeFound`, `PartnerTapeFound` |
| `CDynamicEnvironmentManager` | 16 | `SetDepthOfFieldOverride`, `RemoveDepthOfFieldOverride`, `SetFogOverride`, `RemoveFogOverride`, `SetCloudOverride`, `RemoveCloudOverride`, `SetWindOverride`, `RemoveWindOverride`, `SetLightingOverride`, `RemoveLightingOverride`, `GetScriptedTimeOfDay`, `SetScriptedTimeOfDay`, `SetScriptedStormFactorOverride`, `RemoveScriptedStormFactorOverride`, `SetAdaptiveBloomOverride`, `RemoveAdaptiveBloomOverride` |
| `CDominoManager` | 10 | `SpawnDominoEntity`, `RemoveDominoEntity`, `RemoveCommandEventToEntity`, `SendCommandEventToEntity`, `SendCommandEventToEntity2`, `QueueCommandEventToEntity`, `QueueCommandEventToEntity2`, `SendRegisteredEventToEntity`, `TraceConnection`, `IsScriptAutorunEnabled` |
| `CGameMessageBoxHelper` | 8 | `CreateConfirmationMessageBox`, `CreateTutorialMessageBox`, `CreateCustomTutorialMessageBox`, `CreateFloatingTutorialMessageBox`, `CreateTutorialMessageBoxWithActionMap`, `CreateCustomTutorialMessageBoxWithActionMap`, `CreateFloatingTutorialMessageBoxWithActionMap`, `HideFloatingTutorialMessageBox` |
| `CScriptCallbackSystem` | 8 | `RegisterEventCallback`, `RegisterOnSpawnCallback`, `RegisterOnRemoveCallback`, `RemoveCallback`, `RemoveCallbacks`, `RegisterMessageListener`, `UnregisterMessageListener`, `BroadcastMessage` |
| `CFCXBarkManagerService` | 4 | `LoadMissionBarkBank`, `UnloadMissionBarkBank`, `RegisterMissionBankLoadedCallback`, `UnregisterMissionBankLoadedCallback` |
| `CBaseMission` | 5 | `IsEnabled`, one unnamed-string-literal binding, `Enable`, `Disable`, `SetState` |
| `CDominoDelayManager` | 4 | `CreateDelay`, `RemoveDelay`, `SetDelay`, `SendCommand` |
| `CDominoConsoleCommandManager` | 2 | `RegisterConsoleCommand`, `UnregisterConsoleCommand` |
| `CDominoSequenceManager` | 2 | `CreateListener`, `DeleteListener` |
| `CDominoBoxResource` | 2 | `RegisterBox`, `LoadResource` |
| `CDominoBoxInstance` | 2 | `CreateBox`, `GetParentEntity` |
| `CMusicManager` | 3 | `SetWorld`, `ChangeSet`, `PlayMusicFromLua` |
| `CDominoSoundManager` | 1 | `PlaySound` |
| `CMovieSystem` | 1 | `CommandSequence` |
| `CGameMissionMgr` | 1 | `GetMission` |
| `CFCXRegionManager` | 1 | `IsSafeHouseLocked` |
| `CFCXObjectiveHudManager` | 1 | `PushNewObjective` |
| `CDominoWaterLevelManager` | 1 | `SetWaterLevel` |
| `CTerrain` | 1 | `GetSector` |
| `CSector` | 1 | `SetWaterLevel` |
| `CEntityComponent` | 1 | `GetEntity` |
| `CVehicle` | 1 | `GetCurrentVehicle` |
| `CFCXGameplayManager` | 1 | `SetMapArmy` |
| `CJackalTapeManager` | 1 | `StopTape` |

Each of these manager classes also exposes a much larger set of **plain data properties** (not
functions) through a separate, parallel reflection mechanism (`FUN_1029b000`/property-descriptor
structs with `GetNameHash("PropertyName", ...)`, visible interleaved in the same registration
functions — e.g. `CFCXMissionManager` also exposes `CurrentAct`, `MissionTime`,
`InfMinCoefficient`, etc. as readable/writable fields). Those aren't part of this Lua *function*
survey but are readable from the same decompiled registration functions if needed later.

**Not yet explored**: one registration call site (`0x10738c38`) sits outside any function Ghidra has
identified as a `Function` object — needs a manual Disassemble+Create-Function pass in the GUI before
it's readable via MCP. Every other one of the ~260 sites has been surveyed.
