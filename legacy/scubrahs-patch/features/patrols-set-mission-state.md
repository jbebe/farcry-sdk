---
title: SetMissionState box with patrol, convoy and buddy guards
kind: shared
status: located
systems: [patrols, missions, buddies]
match:
  - "domino/system/setmissionstate.lua@*"
exclude: []
requires: []
verified: diff
---

# SetMissionState box with patrol, convoy and buddy guards

A shared rewrite of the stock `SetMissionState` Domino box, the box every script uses to enable,
disable or turn off a mission. Its hunks interleave several features, so it is one page; the
largest part keeps the randomized patrols off convoy routes and out of the opening.

## How

`domino/system/setmissionstate.lua` (every hunk):

- **Patrols** (for `randomized-patrols-core`, which moved the vanilla patrols out of the
  `convoy_0N_disable` and opening-sequence layers that used to hide them):
  - `Enable` refuses to enable a `GhostPatrols` mission of the groups that run along a convoy's route
    while that `Missions/ConvoyMissions/Convoy_0N` is enabled ("Update 3.4"), matching group ids in
    the mission name (17 ids over the four convoys);
  - `Enable` refuses any mission whose name contains `GhostPatrol` while
    `Globals.MASTER_GameGlobals.FinishedIntro` is `0`;
  - after enabling a `Convoy_0N` mission, `Enable` calls a new `DisableConvoyPatrols()`, which
    disables every `BluePatrol`/`RedPatrol` mission of those groups, per world.
- **Convoys**: `Enable` refuses `Missions/ConvoyMissions/ArmsMerchant` while any `Convoy_01`-`04` is
  enabled, so the merchant is not offered again mid-convoy.
- **Primary missions**: `Enable` sets `PrimaryMissionActive = 1` for five story missions that start
  without being accepted (`A3SM14` airport, `A1SM03` journalist, `A3SM15`, `A2SM05`, `A3SM13`), and
  for the `A3SM11` warlord missions also disables the leftover `A2SM06` lieutenant layers.
- **Buddy missions**: `Off` ignores a `BuddySideQuests` mission (and then does not fire `Out`) unless
  the accepted mission is `A1SM02`, where it clears `AcceptedBuddyMissionID` and lets it go.
- Drops the `DOMINO REFLECTION BOX` header and the "left empty on purpose" comments, adds log lines.

## Depends on

The globals `FinishedIntro`, `PrimaryMissionActive`, `AcceptedMissionID` and
`AcceptedBuddyMissionID` are declared in `domino/user/master_gameglobals.globals.lua` (`@L27`,
`@L90`); `FinishedIntro` is set to `1` in `domino/user/a1sm01_townescape.a1sm01_mission.lua`. Those
are on other pages. Without the declaration, `FinishedIntro == 0` is false and the intro guard does
nothing.

## Uncertain

- The convoy, primary-mission and buddy guards belong to the Concurrent Missions and buddy fixes;
  which published lines they serve is for those pages to say.
