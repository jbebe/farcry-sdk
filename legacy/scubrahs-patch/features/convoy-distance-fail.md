---
title: Convoy missions no longer fail on distance
kind: component
bundle: fixes
claims:
  - "Fixed an issue where some convoy based missions could \"fail\" once the target vehicle got a certain distance from the player"
status: located
systems: [missions, ai]
match:
  - "domino/system/convoymission.lua@*"
  - "worlds/*/generated/world*.mapsdata.fcb/*#Components/CFCXAIComponent/AIObject/fEscapeRange"
exclude: []
requires: []
verified: diff
---

# Convoy missions no longer fail on distance

A convoy or vehicle-target mission no longer fails when the target vehicle gets far from the player.

## How

- `CConvoyMission` controllers in `world2.mapsdata.fcb`: `CFCXAIComponent/AIObject/fEscapeRange`
  `250` -> `0` on `ConvoyMission_0`, `ConvoyMission_3` (`2055018054720435157`) and
  `AssassinationMission_Target03` (a vehicle-target assassination). The world1 controllers already
  have `0` in the base game.
- `domino/system/convoymission.lua@L55`, `@L56` ("Update 3.3: Prevent convoy missions from
  'failing'"): the `ConvoyMission` box's `Event_Failure` handler, which runs on the entity's
  `FailedConvoy` event, now unregisters and returns `Success()` instead of `Failure()`.

## Uncertain

- That `fEscapeRange` is the distance at which the convoy counts as escaped is read from its name and
  the claim. The script change goes further: every `FailedConvoy`, whatever its cause, now reports
  success, which also makes the `@L79` rewiring of `convoy-truck-completion` unreachable.
