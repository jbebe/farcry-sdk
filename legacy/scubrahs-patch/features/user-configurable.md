---
title: External configuration file
kind: component
bundle: features
claims:
  - "User Configurable: Tweak various mod features to your liking within the external configuration file"
status: located
systems: [missions, ui]
match:
  - "install/scubrahspatch.lua"
  - "_hash/13e95c15.bin"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_onload.*"
  - "domino/system/setmalaria.lua@L77"
  - "domino/user/a1bu00_tutorial.a1bu00_weaponshop.lua@L116"
  - "domino/user/a1bu00_tutorial.a1bu00_storymission.lua@L5872"
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L2204"
  - "worlds/*/generated/entitylibrary.fcb/gadgets/equipped/map_nomarker.xml"
exclude: []
requires: [missions-game-globals, graphics-environment-manager]
verified: diff
---

# External configuration file

`ScubrahsPatch.lua` in the game folder holds eight settings the player edits in a text editor. They
are read every time a world loads, so a change applies to an existing save.

## How

**The file.** `install/scubrahspatch.lua` (`Far Cry 2/ScubrahsPatch.lua` in the archive) is plain
Lua: one commented assignment per setting to `Globals.MASTER_GameGlobals.<name>`, with its default
and valid range. The same defaults are declared in the globals table (`missions-game-globals`), so
they apply if the file is missing (inferred: a failing `dofile` only stops `onload.lua`).

**The loader is data, not a `Dunia.dll` patch.** Each world's `omnis.fcb` gains an omni entity
`DominoOmniEntity_OnLoad` whose `CDominoComponent` runs `fileBoxPath` `domino\User\onload.lua` with
`hidStartOnLoad` `True` (and a `CPersistComponent`, `selLevel` `3`). That script is stored nameless as
`_hash/13e95c15.bin` (its name was recovered from its hash). It logs, calls
`dofile("../scubrahspatch.lua")` - relative to the game's working directory, `bin`, so the file
beside `bin` - and then re-applies the night environment overrides (`Default.ScriptedEvent.Lighting`,
`Default.Disabled.AdaptiveBloom`) according to `NightEnvironmentEnabled`, `DarkerNights` and
`VanillaColorgrading`, which belong to `darker-nights`.

**The settings and what reads them:**

| Setting | Default | Read by |
|---|---|---|
| `OutpostDelay` | `2700` s | `domino/system/delay.lua` resolves `Seconds = "Outpost"` (`outposts-delay-presets`), used by `functional-outposts` |
| `PatrolDelay` | `300` s | the same box, `"Patrol"`, used by `randomized-patrols-core` |
| `WeaponRespawnDelay` | `3600` s | the same box, `"Weapon"`, used by `armory-respawn-cooldown` |
| `ShowPlayerMapMarker` | `1` | this page (below) and `graphics-environment-manager` |
| `FastTravelCost` | `0` | `missions-fast-travel-cost` |
| `EnableMalaria` | `1` | this page (below) and `graphics-environment-manager` |
| `VanillaColorgrading` | `0` | `original-colorgrading`, `graphics-environment-manager`, `onload.lua` |
| `DarkerNights` | `0` | `darker-nights` via `graphics-environment-manager` and `onload.lua` |

**Map marker.** The two places that hand the player the map gadget - the tutorial
(`a1bu00_tutorial.a1bu00_storymission.lua@L5872`) and the town escape
(`a1sm01_townescape.a1sm01_mission.lua@L2204`) - now give `Gadgets.Equipped.Map` when
`ShowPlayerMapMarker` is `1` and `Gadgets.Equipped.Map_NoMarker` when it is `0`, recording the choice
in `EquippedMapGadget` (`1` or `2`). `Map_NoMarker` is a new archetype in both worlds' libraries
(`worlds/*/generated/entitylibrary.fcb/gadgets/equipped/map_nomarker.xml`), the map with an empty
`archPlayerMarker`. The environment manager's loop swaps the gadget when the setting changes later.

**Malaria.** `domino/system/setmalaria.lua@L77`: the `SetMalaria` box applies its sickness level only
when `EnableMalaria` is `1` and sets it to `0` otherwise. The environment manager's loop also calls
`StopMalariaBlackout()` while `EnableMalaria` is `0`, but only once `TutorialFinished` is `1`, which
`a1bu00_tutorial.a1bu00_weaponshop.lua@L116` sets when the tutorial's weapon-shop step completes.
The setting's own comment says scripted story attacks are not affected.

## Depends on

- `missions-game-globals` declares the eight settings and the state fields used here.
- `graphics-environment-manager` (`_hash/2faa683c.lua`) holds the runtime half of the map-marker and
  malaria settings and applies `VanillaColorgrading`.
- The delay settings do nothing without `outposts-delay-presets`; the GPS and compass variants of the
  no-marker map belong to the diamond tracker and GPS/compass pages.

## Uncertain

- That `dofile` resolves `..` against `bin` is an inference from the path; the mod works with the file
  in the game root.
