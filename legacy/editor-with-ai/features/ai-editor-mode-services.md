---
title: Single-player AI services in the editor's game mode
kind: component
bundle: editor-war-ai
status: located
systems: [ai, buddies, engine]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameMode[4]/**"
exclude: []
requires: []
verified: re
---

# Single-player AI services in the editor's game mode

This is what makes placed characters come alive. When a map is played from the map editor, the game
runs it in the `FCXEditor` game mode. That mode ships with only multiplayer services. The mod adds
the single-player AI stack to it, so soldiers, buddies and civilians placed with
[`ai-people-palette`](ai-people-palette.md) think, fight, talk and take damage as they do in the
campaign.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameMode[4]` (`Desc Name="FCXEditor"`). Its `<Services>`
list grows from 25 entries to 68. The first 23 vanilla entries are overwritten in place and 43 are
added; the last two vanilla entries, `CFCXMatchService` and `CFCXUiService`, stay. The `NetPresence`
attributes are dropped.

The engine builds a game mode's services in `CGameMode::CreateServices`, traced in the symbolized
`FarCry2_server`. For each `<Service>` it asks `CGameModeServiceFactory::Create` for the class. The
factory returns an already-built shared service for that id, or calls the creator registered for
it. A name with no registered creator returns null, and `CreateServices` skips it without a
message. The PC loader of this list is `LoadProperties` (GOG `Dunia.dll` `0x1056AAD0`). It also
drops a second `<Service>` whose class name hashes the same as an earlier one (`FUN_1056A660`), so
a repeated `ClassName` counts once.

Read against the registered creators (`RegisterServices` at `0x08961140` and `0x093780E0`, and
`CGameModeManager::Init`, all in the server build), the rewritten list does three things:

- **Adds the single-player AI services**, each with the property block the `FCXSingle` mode uses:
  `CFCXGameplayManager` (`GameplayManagerService`), `CFCXAIBehaviorService`
  (`FCXAIBehaviorService`), `CBuddiesManager` (`BuddiesManagerService`), `CFCXBarkManagerService`
  (`SPBarkManagerService`), `CSpawnPointService`, `CDominoService` (`DominoService`),
  `CBonusService` (`BonusService`) and `CWeaponBazaar` (`WeaponBazaar`).
- **Switches two existing services to their single-player blocks.** `CFCXCountersService` uses
  `DefaultCountersService`, the campaign's health, damage and hit-location model, instead of
  `MPCountersService`. `CFCXMissionManager` is given `MissionManagerService`.
- **Lists 33 names that are not services.**
  - Entity components and entity classes: `CFCXAIComponent`, `CFCXPlayer`, the five
    `CPawnBeautifier*AI`, `CBasicShapeEntity`, `COmniEntity`, `CFCXEditorSplineZone`, the five
    `CFCXCountersComponent*` and `CFCXParticleAmbianceComponent`.
  - A world's managers and engine singletons: `CDominoManager`, `CDominoConsoleCommandManager`,
    `CPrefabManager`, `CMusicManager`, `CMusicAIInfoManager`, `CJackalTapeManager`,
    `CWaterSoundManager`, `CZoneLogicManager`, `CRadioManager`, `CBulletTracerManager`,
    `CRealtreeFxManager`, `CFCXWorldDemoManager`, `CSmartTerrainManager`, `CScoutIntelsManager`,
    `CDiamondsManager` and `CCollectionManager`.
  - `AIAndPlayer`.

  None has a service creator, so all are skipped. `CFCXPlayerService` and `CFCXGameSettingsService`
  are each listed twice, and the second copy is dropped.

Against `FCXSingle`, the editor mode still lacks `CRescueManager` (buddy rescues),
`CPersistenceMgr` and `CFCXSingleGameFilesService`.

Only `FCXEditor` changes. The multiplayer modes (`FCXDeathMatch`, `FCXTeamDeathMatch`, `FCXCTF`,
`FCXVIP`) keep their vanilla services, so AI placed in a map runs in the editor's play-test, not in
a multiplayer match on that map.

The added services read property blocks that the campaign also uses. The mod edits several of those
blocks too; their pages are in the `gameplay` bundle.

## Uncertain

- The registered-creator list was read from the Linux server build. The PC client registers a few
  services the server lacks: vanilla `FCXEditor` lists `CFCXOnlineMapService`, which is in neither
  server list. The non-service names above are components, entity classes and world managers on
  any build, but they were not checked one by one in `Dunia.dll`.
- AI needs a navmesh to move. Whether AI in an editor map paths, or only stands, turns and shoots,
  has not been checked. The map editor has no navmesh export, and nothing in this mod adds one.
- Dropping `NetPresence` only matters to a client in a networked editor session. Such a client
  creates only services with a presence set, so it would build none of these.
