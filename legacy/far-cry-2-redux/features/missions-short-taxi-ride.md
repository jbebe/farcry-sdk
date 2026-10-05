---
title: Short taxi ride
kind: component
bundle: missions
claims:
  - "Mod updated to include options for full/short taxi ride and player position on the map"
status: located
systems: [missions]
match:
  - "domino/user/openingsequence/openingsequence.taxiride.lua@*"
exclude: []
requires: []
verified: diff
---

# Short taxi ride

The opening taxi ride is cut from about four and a half minutes to about one: the malaria attack
comes 44 seconds in, and the white-out that ends the ride at 61 seconds. This page is the taxi half
of the readme line; the map half is `nav-no-player-position`.

## How

`domino/user/openingsequence/openingsequence.taxiride.lua`, the taxi ride graph, which drives the
scene from `SequenceTimer` boxes keyed to seconds into the `OpeningSequence` sequence. Three hunks:

- `@L304`: the 44 s timer (box `87`) runs the handler of the 266 s timer (`f_261_TimeReached`)
  instead of its own: the malaria attack sound and the `malariamajorattackfx` post effect, in place
  of three civilians' animations.
- `@L289`: the 58 s timer (box `104`) runs the handler of the 278 s timer (`f_271_TimeReached`):
  the closing sound and a 3 s delay to the `whitescreenfx` white-out, in place of two characters
  getting into a vehicle.
- `@L1384`: after the white-out, the `Delay` box `17` waits `2` s instead of `5` before enabling
  `Missions/OpeningSequence/DisableForOpeningSequence` and handing over (the old value is kept as a
  `--5` comment).

The rest of the ride's timeline - the drive, the driver's talk, the checkpoint - plays only for as
long as the first minute lasts.

## Full Taxi Ride variant

The readme's choice is made by swapping `patch.dat`. The Full Taxi Ride / No player position
archive carries no `domino/user/openingsequence/` script at all, and every other file in it is the
same as in the analysed Short Taxi Ride / No player position archive. So the full variant plays the
vanilla ride: malaria attack at 266 s, white-out at 281 s, a 5 s wait after it.

## Compared with other mods

Scubrah's [`skippable-intro`](../../scubrahs-patch/features/skippable-intro.md) and
[Skip Intro](../../skip-intro/features/skip-opening-sequence.md) skip the ride entirely and start
`A1SM01` at the hotel; this mod keeps a shortened ride.

## Uncertain

- Whether the timers between 58 s and 266 s still fire behind the white-out, before the sequence
  is stopped, is not traced; their handlers are animations and sounds of a scene the player no
  longer sees.
- The original 266 s and 278 s timers keep their handlers; if the sequence ran that long they would
  fire a second time. The hand-over at 63 s makes that unlikely (inference).
