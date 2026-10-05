---
title: RaZoR's Domino script samples for editor maps
kind: component
bundle: editor-scripting
status: located
systems: [missions, engine]
match:
  - "_hash/7fed6e1a.bin"
  - "_hash/a6654997.bin"
  - "_hash/b5d648a7.bin"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# RaZoR's Domino script samples for editor maps

Three Lua scripts that run inside an editor map. They spawn objects, react to a pickup, show a
message box, spawn characters and change the water level.

## How

Each script is a Domino box (`export = {}` with an `Init`) attached to an entity through a
`CDominoComponent`. The component's `fileBoxPath` names the script. Three archetypes in the mod's
custom library ([`editor-razor-library`](editor-razor-library.md)) carry one each:

| Archetype | `fileBoxPath` | File |
|---|---|---|
| `Custom.MissionScripts.JanneMapScript` | `DLCDomino\Community\RaZoR\JanneMap.lua` | `_hash/7fed6e1a.bin` |
| `Custom.MissionScripts.TestScript` | `DLCDomino\Community\RaZoR\FrankBildersTest.lua` | `_hash/a6654997.bin` |
| `Custom.MissionScripts.SampleScript` | `DLCDomino\Community\RaZoR\DominoSample.lua` | `_hash/b5d648a7.bin` |

The names were recovered by hashing each `fileBoxPath` (CRC32 of the lowercased path), which
matches the archive entry.

- **`JanneMap.lua`**: on `Init`, spawns `OA_MissionPickups.MissionPickups.CellTowerPanel` at a
  fixed spot (99.35, 274.55, 33.13). It registers a `DominoCallbackPickupPicked` callback through
  `CScriptCallbackSystem_GetInstance():RegisterEventCallback`. Picking the panel up opens a
  confirmation box (`CGameMessageBoxHelper_GetInstance():CreateConfirmationMessageBox`, title and
  text "WARNING"). Yes spawns `player.MainCharacter.PawnPlayerNetworkKit.Assault.Level1` at
  (239.08, 343.66, 16.35), in a local named `AssassinationTarget`. The coordinates fit one map,
  presumably Janne252's.
- **`FrankBildersTest.lua`**: spawns `pickups.Weapons.AK47Mod` at the map centre (256, 256, 16.5).
  On pickup it should spawn the library's `CustChars.RaZoR.NetworkFrankBildersTest` there. A
  commented-out alternative fires the same event from a 15 s `CDominoDelayManager` delay.
- **`DominoSample.lua`**: draws "Test" with `System:DrawDebugText2D` and sets the water level of the
  template world's sectors 1 to 12 to 64 (`CDominoWaterLevelManager_GetInstance():SetWaterLevel`).
  A commented-out line tries the same through `CTerrain`.

None of the three archetypes has a palette entry. A map runs a script only if its file already
places that entity, so these serve particular community maps rather than the editor's own palette.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) declares the three script-carrying archetypes
  and the Frank Bilders character. That library is `dlc2\dlc2_entitylibrary.fcb`, and nothing in
  this archive registers it. With this mod alone, no map can place the script archetypes, and the
  scripts never run. They need the separate "DLC2" registration the mod's own strings ask for
  ("DLC2 required Ingame!").
- `CDominoService`, which [`ai-editor-mode-services`](ai-editor-mode-services.md) adds to the
  editor mode, is probably what runs Domino components there. Vanilla `FCXEditor` has no Domino
  service.

## Uncertain

- `FrankBildersTest.lua` registers its callback on `instance`, a global it never defines. Unless
  something else defines it, `Init` stops there and picking up the AK-47 spawns nothing.
- Whether a Domino component runs in the editor without `CDominoService` is not checked, so neither
  is whether these scripts predate Princeton73's change.
