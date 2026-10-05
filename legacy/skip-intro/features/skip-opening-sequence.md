---
title: Skip the opening sequence
kind: component
bundle: skip-intro
claims:
  - "Skip the opening sequence: a new game starts the town escape mission at the hotel, without the taxi ride"
status: located
systems: [missions]
match:
  - "domino/user/master_world1.world1.lua@L{799,801}"
exclude: []
requires: []
verified: diff
---

# Skip the opening sequence

A new game opens in the hotel room in Pala, at the start of the first story mission (`A1SM01`, the
town escape). The taxi ride, the check-in and the malaria collapse never play.

## How

`domino/user/master_world1.world1.lua`, the world 1 master graph. In vanilla, its `In` calls
`CheckAutorunEnabled`, which fires on a new game. Its `f_1_Out` enables the `blackscreenfx` post
effect, and the PostFx box's `Out` goes to `f_0_Out`. That starts
`OpeningSequence.TaxiRide.lua`, which fades the black screen out. Two lines of `f_1_Out` change:

- `@L801`: the PostFx box's `Out` is `f_12_Out` instead of `f_0_Out`. `f_12_Out` is the vanilla
  handler of the graph's `A1SM01` developer console command. It runs `SetCurrentMission` `A1SM01`
  and then starts `A1SM01_TownEscape.A1SM01_Mission.lua`.
- `@L799`: the effect name is `xlackscreenfx` instead of `blackscreenfx`. No effect has that name,
  so the screen is not blacked out. Without the taxi ride, nothing would turn a black screen off
  again. The PostFx box still fires `Out` after calling `EnablePostFx`.

The opening sequence has its own developer skip, a `SkipOpeningSequence` console command in the
taxi ride graph. The mod does not use it.

`jackall-cli mod lint` reports `identity-conflict`, which is error level, and `stale-twin` here.
BlackBox names each handler after the box it serves. Wiring the pooled PostFx box to `f_12_Out`
therefore makes box 12 look like both `ConsoleCommand` and `PostFx` to the graph view. At runtime it
is a plain call: `f_12_Out` only reads `self._graph`, which the PostFx box has. The `.debug.lua` twin
is not updated.

## Uncertain

- That `EnablePostFx` ignores an unknown name instead of raising a Lua error is inferred from the
  mod working. If it raised one, the PostFx box would never reach `Out`. The binding was followed
  into `Dunia.dll` but not to the lookup.
- What the taxi ride sets up besides the scene never runs. It sets the time of day to 9:30. It turns
  off the `Missions/_DisableForMission` layers `W1C3_RoadPatrols`, `W1C3_DFZ_AI` and `PGP_AI`, and
  `Missions/OpeningSequence/TemporaryWorkLayer`. It also enables `ScriptedAIMode`. Whether the town
  escape relies on any of this has not been checked. Scubrah's Patch 3.7 adds
  `DisableScriptedAIMode()` at the mission's start for this path.
