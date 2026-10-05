---
title: Skippable intro
kind: component
bundle: features
claims:
  - "Skippable Intro: Allows you to skip the long intro sequence and start the game at the hotel in town"
status: located
systems: [missions, ui]
match:
  - "domino/user/master_world1.world1.lua@L{58,557,686,887,888}"
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{721,1785,1814}"
  - "domino/user/openingsequence/openingsequence.taxiride.lua@L1532"
  - "languages/*/oasisstrings.fragment.xml#MessagesBoxContents/SKIP_INTRO_TEXT"
  - "domino/system/postfx.lua@L{32,39}"
exclude: []
requires: [missions-game-globals, functional-outposts]
verified: diff
---

# Skippable intro

A new game opens on a yes/no box, "Do you want to skip the intro sequence?". Yes skips the taxi ride
and the malaria collapse and starts the first story mission at the hotel in Pala; No plays the intro
as usual.

## How

All in Domino scripts, no engine change:

- `domino/user/master_world1.world1.lua` - the world's master graph. `@L58` registers the stock
  `PopUpConfirmationMessageBox` box and `@L557` creates it as box `400`, `Yes` wired to a new
  `f_12_Out_Skip`, `No` to the existing `f_0_Out` (the opening sequence). `@L686` replaces the
  vanilla `CheckAutorunEnabled` call at the start of a new game: it disables the Functional Outposts
  layer `Missions/Outposts/w1_c_3/zXmOLgx` (the outpost in Pala), enables the post effect
  `blackscreennowfxhighpriority` and opens box `400` with `SKIP_INTRO_TITLE` / `SKIP_INTRO_TEXT`.
  `@L887`/`@L888` add `f_12_Out_Skip`: set `SkippedIntro` to `1` and run `SetCurrentMission`
  `A1SM01` directly, the step the opening sequence normally ends with.
- `domino/user/openingsequence/openingsequence.taxiride.lua@L1532` - the taxi ride turns the black
  screen off again when the player chose No.
- `domino/user/a1sm01_townescape.a1sm01_mission.lua` - the town escape mission. `@L721`, at its
  `In`: set `FinishedIntro` to `1`, disable `blackscreennowfxhighpriority`, re-enable the Pala outpost
  layer, and (update 3.7) `DisableScriptedAIMode()` so AI is not left scripted when a player
  quits the intro and skips it on a second try. `@L1785` and `@L1814`: when `SkippedIntro` is `1`,
  remove two cutscene props and cut the hotel waits (boxes `150`, `166`, `169`: 96 s, 8.15 s and
  5 s) to 0.1 s.
- `SKIP_INTRO_TEXT` is added in all nine languages. `SKIP_INTRO_TITLE` is not added anywhere.

## Depends on

- `missions-game-globals` declares `SkippedIntro` and `FinishedIntro`.
- `functional-outposts`: the setup calls `GetMission(...):Disable()` on the Pala outpost layer
  without a nil check, so the skip box never opens if that layer is missing.
- Two related changes sit on other pages: `domino/system/postfx.lua@L32` and `@L39` make the `PostFx`
  box ignore `blackscreenfx` until `FinishedIntro` is set (they serve this feature but are assigned to
  the graphics pages), and `patrols-set-mission-state` keeps ghost patrols off until
  `FinishedIntro` is `1`.

## Uncertain

- Whether a missing `SKIP_INTRO_TITLE` shows an empty or a raw title is not checked.
- The hotel's clock change (11:00 to 9:00) is on `missions-opening-clock`, not here.
