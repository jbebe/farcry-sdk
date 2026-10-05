---
title: Dynamic environment manager script
kind: shared
claims: []
status: located
systems: [environment, graphics, ui, player]
match:
  - "_hash/2faa683c.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_dynamicenvironmentmanager.*"
exclude: []
requires: []
verified: diff
---

# Dynamic environment manager script

A looping Domino script that switches environment presets by the time of day. It is the runtime half
of `morning-fog`, `darker-nights` and the vanilla-grading option of `original-colorgrading`, and it
also carries two unrelated settings checks.

## How

`_hash/2faa683c.lua` (`domino\User\environmentmanager.lua`, header "Dynamic Environment Manager
(based on time of day)", by scubrah, updated in 3.6 and 3.7) is hosted by a new omni entity in each
world, `DominoOmniEntity_DynamicEnvironmentManager` in `world1.omnis.fcb` and `world2.omnis.fcb`
(`CDominoComponent` `fileBoxPath` `domino\User\environmentmanager.lua`, `hidStartOnLoad` False,
persistence level `Critical`).

On `In` it decides which world it is in from whether the mission `A1SM01_StreetFighting` exists,
then runs a one-second `Delay.lua` loop that reads `GetScriptedTimeOfDay` and:

- **Morning fog.** From 05:00 to before 08:00, unless the current fog override
  (`MASTER_GameGlobals.FogOverride`) is a sandstorm or defoliant one, it sets the fog override
  `Default.Morning.Fog` with a 30 s transition and marks `MorningEnvironmentEnabled`. From 08:00 to
  05:00 it removes it, except while the world's ceasefire mission (`A1SM01_StreetFighting` or
  `A2SM06_CapitalChaos`) is enabled.
- **Night.** From 22:00 to 06:00 it sets the lighting override `Default.ScriptedEvent.Lighting`
  (30 s) and marks `NightEnvironmentEnabled`; from 06:30 it removes it. While
  `DarkerNights` is 1 and `VanillaColorgrading` is 0 it also sets and removes the adaptive-bloom
  override `Default.Disabled.AdaptiveBloom` (1 s) with the lighting.
- **Vanilla grading.** While `VanillaColorgrading` is 1 it sets the adaptive-bloom override
  `Default.Original.AdaptiveBloom` every tick.
- **Map gadget** (update 3.6). Swaps the equipped map gadget between the marker and no-marker
  variants (`gadgets.Equipped.Map`, `Map_GPSUpgrade`, `Map_RealCompass` and their `_NoMarker`
  twins) to follow `ShowPlayerMapMarker`, through `ManageInventory.lua`.
- **Malaria** (update 3.7). While `EnableMalaria` is 0 and the tutorial is finished, calls
  `StopMalariaBlackout` every tick.

## Depends on

- The `MASTER_GameGlobals` fields it reads and writes (`FogOverride`, `MorningEnvironmentEnabled`,
  `NightEnvironmentEnabled`, `DarkerNights`, `VanillaColorgrading`, `ShowPlayerMapMarker`,
  `EquippedMapGadget`, `GPSUpgradePurchased`, `EnableMalaria`, `TutorialFinished`), declared in
  `domino/user/master_gameglobals.globals.lua` and set from `ScubrahsPatch.lua`.
- The presets it names, added by `morning-fog`, `darker-nights` and `original-colorgrading`.

## Uncertain

- No script names the entity, and like 73 of the mod's 74 new `world1` omni hosts it has
  `hidStartOnLoad` False; how its `In` gets called is not traced.
- A plain-text Lua unit, `_hash/13e95c15.bin` ("OnLoad.lua: Loaded"), runs `../scubrahspatch.lua`
  and re-applies or removes the night lighting and `Default.Disabled.AdaptiveBloom` overrides from
  `NightEnvironmentEnabled` on load. It is left to the page that owns the settings file, but night
  lighting after a reload depends on it.
