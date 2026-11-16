---
sidebar_position: 8
---

# Settings and Scripting

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`, the symbolized Linux dedicated server (read only in Ghidra); every
address is the server's unless marked PC, meaning the Steam `Dunia.dll`. Settings are looked up by
hash, which is plain CRC32 of the name, and the PC build was matched by hash for the three
game-mode settings below. Data read from the retail 1.03 `patch` over `common` trees. **Nothing here
was seen in game.** Where an effect is read from a gate and not from a run, the text says so.
:::

The engine reads a number of switches the shipped data leaves at a default, sets, or never
mentions, and it registers script bindings no shipped script calls. Each item says what exists,
what keeps it off, and what turning it on takes. The console and its gates are in
[the developer console](../engine-internals/developer-console.md), the Lua surface in
[the Lua API surface](../engine-internals/lua-api-surface.md), Domino in
[Domino scripts](../engine-internals/domino-scripts.md) and the profile's cheat flags in
[cheat flags and economy](../engine-internals/cheat-flags-and-economy.md).

## Game-mode settings

`engine/gamemodes/gamemodesconfig.xml` (in `patch`) holds a `SettingsCollection` per container.
`FCXSingle`, the campaign, inherits the `Single` and `Common` containers.

| Setting | Retail value | Reader | Flip it and |
|---|---|---|---|
| `vehicle_passenger_forbidden` | `Single` 1 (bool) | vehicle seat code | the player can sit in a passenger seat |
| `drop_weapon` | `Common` 0 (uint) | `CExplosive::OnEvent` | placed explosives answer "usable" |
| `dynamicLifeTime` | `Common` 30 (seconds) | pickup spawn | dropped weapons and gadgets live longer |
| `use_pickup_containers` | `Single` 1, `Common` 1 | `CPickupContainer` | weapon crates stop spawning pickups at 0 |
| `revive_enabled` | `Common` 0 | multiplayer game rules only | nothing in single player |
| `specialLodKillScale` | 1.2 on the campaign modes | no server reader | unknown, client side |

All are data edits in a layer's `gamemodesconfig.xml`.

### `vehicle_passenger_forbidden`

The setting (hash `0x32AA64FE`) is 1 for the campaign. Three places read it, and all three have
PC counterparts matched by hash.

- `CVehicle::ValidateCurrentSeat` (server `0x08EE6510`): a non-AI pawn in a passenger seat (seat
  type 3) is moved to another seat through `SVehicleImpl::ChangeSeat` while the setting is true.
- `ChangeSeat` (server `0x08EFF7xx`, read near `0x08EFFCED`): the player's seat-cycle key tries free
  driver seats (type 1), then gunner (2), then passenger (3), and on reaching a passenger seat
  returns without moving him.
- `CGOStateEventInventory::TryAutoDrawWeaponFromVehicle` (server `0x0933124E`) returns early while
  the setting is true. At 0, a player with automatic draw gets his weapon drawn from the seat.

At 0 the player can take a passenger seat, which matters for any ride someone else drives: scripted
rides (`BindToVehicleRide`), convoy scenes, or an AI driver added by a mod. The seat numbers are
read off the seat search order. How a passenger seat animates for the player was not checked.

### `drop_weapon`

Hash `0x5BD0410D`. In single player its only reader is `CExplosive::OnEvent` (server `0x0902C360`)
on the usable query. At 0 the explosive answers `SetUsable(false)` before any use text is set, so a
placed explosive cannot be picked up with the use key. Non-zero falls through to a `CanContain`
check on the user's inventory and, if it passes, an `AddContainer`. The other reader,
`CPawnNetworkComponentHostImpl::DoEnterRagdoll` (a drop on death), is multiplayer. Which
single-player entities are `CExplosive` was not enumerated.

### `dynamicLifeTime`

`SInventoryViewPawnImpl::SpawnPickupFromArchetype` (server `0x08FFD310`) calls
`CPickup::SetAutoDestruction(30)` when its auto-destroy argument is set. `DropWeapon` (server
`0x09005DE0`) and `DropGadget` (server `0x09002F30`) pass it set, so in single player every weapon
a pawn drops, including those a soldier drops on death, and every dropped gadget is flagged to disappear
after the value in seconds. The only caller that passes it clear is the wrapper at `0x08FEB030`,
used by the Domino `spawnmissionitem` box. Raising the value keeps drops. That 0 means no expiry is
likely, not verified, and the counter that consumes the stored time was not read. The same limiter
that deletes corpses and debris is in [Physics and Explosions](./physics-and-explosions.md).

### `use_pickup_containers`

Hash `0xB90FCC87`. `CPickupContainer::ActivateOnFinalize` (server `0x08B84710`) activates the crate
when it is 1 and `DeActivate` (server `0x08B83B40`) removes the contained pickup otherwise;
`OnValueChanged` (server `0x08B84E50`) switches it live. A container is a crate that spawns random
pickups (`GeneratePickupToSpawn`, `fRespawnTime`). It exists on 36 archetypes: the multiplayer
`WeaponCrate`s and 26 `pickups.WeaponsCrate.*Crate` archetypes (`AK47Crate`, `AS50Crate`, and so
on). The 26 are in the library and placed in no world 1 or world 2 sector, so the setting has no
visible effect in retail single player. A mod that places one gets a working crate at the shipped 1.

### `revive_enabled`

Read only by `CGRStateAdversarial::IsReviveEnabled`, a multiplayer game-rules state. `Common`
defaults to 0 and the multiplayer modes set 1. No single-player effect.

### `specialLodKillScale` and `DisableDynLoad`

`CGameModeDesc::LoadProperties` (server `0x093F8670`) reads exactly four attributes of a mode:
`Name`, `GameModeEntityArchetype`, `DisableDynLoad` (bool) and `specialLodKillScale` (float, 1.2 on
the campaign modes). All nine server callers of `GetCurrentGameModeDescription` that touch the
descriptor read the name or `DisableDynLoad`, which picks `LoadRings::ResetAll` over `Init` in the
world loader. No server code reads `specialLodKillScale`; the server has no renderer, and the PC
consumer was not found. By its name it scales the render config's `KillLodScale` in campaign modes
(not confirmed). `DisableDynLoad` is 1 on the editor and every multiplayer mode.

### `CScriptService`

`gamemodesconfig.xml` documents `<Service ClassName="ScriptService" ScriptFileName=""
TableName=""/>`, and `LoadProperties` reads both into the service description. `CScriptService` is
linked into the server (constructor `0x094073F0`, used by `CGameModeManager::Init`). Only the
unit-test modes at the end of the file name it, and their script
`Scripts/Engine/GameModes/ScriptService.lua` does not ship. It would be a per-mode Lua service
added to `FCXSingle` by data, a campaign-wide script that needs no mission graph. Its update hooks
and its PC presence were not checked.

## Game profile settings

The `GamerProfile` block's `GameProfile` is `CGameConfig`, registered in
`CGameConfig::RegisterProperties` (server `0x091143D0`); the PC strings are at `0x10E10900` to
`0x10E10F64`. The cheat flags `GodMode`, `UnlimitedAmmo` and `IgnorePlayer` are in
[cheat flags and economy](../engine-internals/cheat-flags-and-economy.md). Two others have a traced
reader and no data sets them:

- **`UnlimitedReliability`** (field `+0x8C`, default 0). `CWeapon::ApplyShootReliability` (server
  `0x08F44E30`) returns at once when it is set, so a shot never lowers a weapon's reliability and
  jams from wear stop growing. The related readers are `CWeapon::Shoot` and `OnEvent`.
- **`AllWeaponsUnlock`** (field `+0x90`, default 0). `CWeaponBazaar::IsItemUnlocked` (server
  `0x08B00CC0`) treats an item that needs unlocking as unlocked when it is non-zero; otherwise it
  asks `CEconomyComponent::UnlockedItem`. The world-availability checks still apply, so every
  gun-shop item locked by progress is buyable from the start.

Whether the retail profile loader reads either name from a user-edited profile was not run; the
members are registered, so a plugin setting the integer is the sure route.

Other members and their defaults: `TimeScale` 6.0, `Hour` 15, `Minutes` 20, `StormHour` 0,
`WindForce` 80, `DelayShadowMovement` 180, `Autosave` 1, `UseCompassMiniMap` 1,
`UseRoadSignHilight` 1, `UseAimingHelpers` 1, `Smoothness` and `Smoothness_Ironsight` 1.0,
`HelpCrosshair` 0, `UseSoundEventOverride` 0, `SkipIntroMovies` 0 (read by
`CFCXMoviePage::Update`, server `0x08DF01B0`), `DisplayAILimitInfo` (read in `CAIWorld::CoreRun`),
`PartnerMissionUnlocked` and `MachetesUnlocked`. `IsMachetesUnlocked` (server `0x09113130`) is
called only from the options menu, and `IsPartnerMissionUnlocked` (server `0x09113110`) reads byte
`+0xFC` and is exposed to Lua. The PC build keeps the machete unlock in the registry
(`Software\Ubisoft\Far Cry 2`, `MachetesKey`).

**Keys retail data sets that neither build registers**, so they are never read: `GatherEntitiesForMap`,
`ShowAIInMap`, `ShowDiamondsInMap`, `padSensor`, `Gamepad_vibration`.

**Registered with no direct reader found**: `UseCompassMiniMap`, `UseRoadSignHilight`,
`UseAimingHelpers`, `UseSoundEventOverride`, `Autosave`. The options pages may read them by
reflection. The options text `OptionMenu.GAMEOPTION_MINIMAP` ("Mini-map") and `GAMEOPTION_ROADSIGNS`
("Highlight Road Signs") exists and the options page never shows either; the engine also has
`CRoadSignManager` and `CRoadSign`. Showing the options would need a reader hook for the profile
values.

**An unregistered field, `CGameConfig+0xEC`**, is zero from the constructor and never written. It
gates the full AI update within 10 m of the player (`CPawnAgent::SetFullUpdate`, server
`0x08575040`), the third-person footsteps of AI (`CAISoundAndFXComponent::OnEntityMove`, server
`0x088C3A60`) and `CArmy` setup. It reads as a headless or no-player switch and is constant in
single player.

Each config object has an override path, for example `engine\settings\OverrideGameConfig.xml` (PC
`0x10E11044`). The file does not ship, and where it is looked up was not traced.

### `CHackConfig`

`CHackConfig::SetEnable` (server `0x09C38B00`) has no caller, so the config is always off. When
enabled **and** the current mission is `A1SM01`, `SetCurrentMission` (server `0x09C38B10`) sets
three flags: cheaper animation level of detail (`CAnimationComponent::UpdateLOD`, server
`0x09471E70`), a doubled physics step (`CPhysWorldImplBase::TimeStep`, server `0x09B33030`) and
thinned sensory updates (`CSensorySystemManager::Update`, server `0x0870F750`). It reads as a
console-era performance hack for the Pala street fight. The PC build was not checked.

## Engine globals

Statics that gate behaviour and are written only by debug paths, with the value in the image:

| Global | Address | Value | Reader |
|---|---|---|---|
| `CSectorPreloadResource::ms_enablePreload` | server `0x0A43C528` | 1 | the sector preload resource (`0x096F1402`, `0x096F180F`) |
| `CSectorPreloadResource::ms_enableRelaxed` | server `0x0A43C529` | 1 | the same (`0x096F187A`) |
| `CMusicManager::ms_enable` | server `0x0A4AEBF0` | 0 | `RegisterResources`, `PlayExternalStream`, `PlayExternalMusic` |
| `CFCXBenchmarkModeHelpers::m_aiEnabled` | server `0x0A429AC4` | | benchmark harness |
| `CFCXCountersComponent::m_hackDrawCounters` | server `0x0A4AD530` | 0 | debug draw |

The two preload switches are never written and select world-sector preload and a "relaxed" preload;
what clearing them does was not traced. `ms_enable` is written only by `SetEnable` (server
`0x08849180`), whose caller was not found; it switches the external music path.
`CDispatcherSquadLieutenant::m_fakeReinforcementPos` (server `0x0A49DF98`) is a zero-vector
sentinel, not a switch. The input board builds hashes for `start_rain_demo`, `stop_rain_demo`,
`start_storm_demo`, `stop_storm_demo`, `start_time_demo`, `debugmenu`, `createIssue`, `enable*Dump`
and `recompileShaders` in `InputBoardInputAction::ProcessSystemSignals` (server `0x088A8D30`), and
the handlers are compiled out.

## Lua

The server registers 284 bindings through `RegisterLuaBinding` (server `0x09BCEF50`) and five
objects through `InitializeTemplate` (`Game`, `System`, `Sound`, `Language`, `Script`). Every
retail `.lua` and `.console` file was checked against them. These exist on PC as well.

**Live and called by no retail script:**

| Binding | What it does |
|---|---|
| `Game:AddAIFactToPlayer(name, value)` (server `0x088B65E0`) | sends `CAddSEFactEvent("AddSEFact", fact, value, 10.0 s)` to the player entity: a fact on the player that AI blackboards can read for 10 s |
| `Game:RefillPlayerAmmo()` (server `0x088B7F40`) | `CInventoryViewPawn::RefillAmmo(true)` on the player |
| `Game:IsPointInWater`, `Game:GetWaterHeight`, `Game:CreateVariable` | readers and a console variable |
| `System:StartShadowRangeOverride(a, b, c)` (server `0x093EEA30`), `StopShadowRangeOverride` | overrides the scene renderer's shadow ranges (`CSceneRendererConfig` `+0x54` flag, `+0x58` to `+0x60`); the first argument is read three times |
| `System:StartWindSound`, `StopWindSound` | `CAmbianceManager::SetWindMuted` |
| `Sound:PlaySoundAtPosition`, `Sound:StopSound`, `CMusicManager:ChangeSet` | sound and music |
| `CBuddiesManager:GetBuddyGender`, `AddHistoryPointsToSidequestBuddy`, `CBaseMission:SetState`, `PopUpObjective`, `EnableCheck` | buddy and mission state |

Which facts AI brains read from `AddAIFactToPlayer` was not checked.

**Registered and empty in both builds**, with no effect: `Game:CreateExplosion` (reads `pos` and
`radius`, does nothing, server `0x088B37F0`), `DrawDamageBorderExplosion`, `DrawDamageBorderHit`,
`WriteHudString`, `WriteHudContextualHelp`, `SetObjectiveStatus`, `DisplayModalInstruction`,
`ShowIngameDialog` (reads 5 or 6 arguments and returns -1), `SetLabel`, and
`Game:SetTransitionToLowContrast`, which calls the empty
`CDynamicEnvironmentManager::SetTransitionToLowContrast` (server `0x09156BE0`).

**Called only from QA or unused Domino boxes:** `SetBaseInfamy`, `SetMalariaPillCount`,
`AddDiamonds`, `WasFactionBetrayed`, `GetAcceptedMissionCount`, `GetSubvertedMissionCount`,
`SetUsableEntitySize`, `DeleteEntityInTrigger`, `SpawnPickupMissionItem`, `GetMalariaPillCount`,
`CheatSetRescueBuddy`, the `Bypass*` buddy and mission setters, `SetWinningFaction` and
`CBaseMission:IsEnabled`.

**Wilderness scripts.** Retail ships seven map-editor presets as Lua in
`common/ingameeditor/wilderness/` (`desert_dunes`, `desert_oasis`, `jungle_mountains`,
`jungle_valley`, `savannah_plateau`, `savannah_plateau_valley`, `savannah_waterside`), so the
[wilderness script language](../engine-internals/wilderness-script-language.md) is Lua. No shipped
script calls `GenerateNoiseObj`, `ApplyMin`, `ApplyMax`, `ApplyClip`, `ApplyStretch` or
`GetRandomNumber`. They are editor-only.

## Domino boxes no mission uses

Of 234 `domino/system` node types, 15 appear in no release `domino/user` graph. Each is a working
node over a live binding or event. Turning one on is a Domino graph edit (JackAll).

| Box | What it gives | Engine side |
|---|---|---|
| `usableentity` | make any entity usable or not, resize its use box, fire `OnUse` | `CUsableComponent_Enable/Disable`, callback `Use`, `SetUsableEntitySize` |
| `spawnmissionitem` | spawn an item archetype on a pawn, returns the entity | `SpawnPickupMissionItem`, `DominoCallbackMissionItemSpawned` |
| `playermalariaevents` | fires when the player blacks out from malaria | `MalariaBlackout` |
| `inputlistener` | fires when the player moves | `InputDominoMove` |
| `bedroll` | enable or disable a bedroll (sleep and save point) | `CBedroll_SetEnabled/Disabled` |
| `detectsocialengagement`, `sendsocialeventtopawn` | detect or push a social engagement on a pawn | `SocialEngagementDetected`, `SendSocialEventToPawn` |
| `removeentitiesintrigger` | delete everything in a proximity trigger | `DeleteEntityInTrigger` |
| `wasbetrayed`, `acceptedmissioncount`, `subvertedmissioncount` | branch on faction betrayal and on missions accepted or subverted per faction | `WasFactionBetrayed`, `GetAcceptedMissionCount`, `GetSubvertedMissionCount` |
| `getmissionstate`, `gettimeofday`, `getmalariapillscount`, `randomboolean` | readers | `CBaseMission:IsEnabled`, `GetScriptedTimeOfDay`, `GetMalariaPillCount` |

Seven more are used only by QA graphs (`debugboxes`, gyms, the `ubidays` demos):
`bypass_librarymissionoffer`, `bypass_playerstate` (sets diamonds, malaria pills and base infamy),
`bypass_setmissionsstatus`, `bypass_setwinningfaction`, `bypass_setbuddiesstatus`,
`cheat_setrescuebuddy` and `randomfloat`. The node code was read; the engine handlers behind the
hashed event names are likely and only some survive as strings.

## Uncertain

- Whether `specialLodKillScale` multiplies `KillLodScale` in campaign modes, and where the PC
  consumes it.
- Whether the retail profile loader reads `UnlimitedReliability` and `AllWeaponsUnlock` from a
  user-edited file.
- What the two sector preload switches change when cleared, and what calls `CMusicManager::SetEnable`.
- Whether `dynamicLifeTime` 0 means no expiry, and what the stored time at pickup `+0x44` finally
  deletes.
- PC readers of `UseCompassMiniMap`, `UseRoadSignHilight`, `UseSoundEventOverride` and `Autosave`;
  PC `CScriptService` and `CHackConfig`.
