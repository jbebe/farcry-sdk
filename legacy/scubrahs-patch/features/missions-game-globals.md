---
title: The mod's global script variables
kind: shared
status: located
systems: [missions, save]
match:
  - "domino/user/master_gameglobals.globals.lua@L{27,90}"
exclude: []
requires: []
verified: diff
---

# The mod's global script variables

A shared piece: the declarations, with starting values, of every `Globals.MASTER_GameGlobals`
variable the mod's scripts read and write. Nothing happens in game from this page alone; almost
every scripted feature of the mod needs it.

## How

`domino/user/master_gameglobals.globals.lua` is the vanilla graph that declares the
`MASTER_GameGlobals` table. Two inserted hunks add fields:

- `@L27` - mission bookkeeping for `concurrent-missions`: `AcceptedStoryMissionID`,
  `AcceptedLibMissionID`, `AcceptedSideMissionID`, `AcceptedConvoyMissionID`,
  `AcceptedAssassinationMissionID` (all `"none"`), `AcceptedBuddyMissionID` (`"x"`),
  `PrimaryMissionActive` and `BSQObjectiveState` (`0`).
- `@L90` - 29 fields at the end of the table:
  - the eight player settings with their defaults, which `install/scubrahspatch.lua` overwrites on
    every load (`user-configurable`): `OutpostDelay` `2700`, `PatrolDelay` `300`,
    `WeaponRespawnDelay` `3600`, `ShowPlayerMapMarker` `1`, `FastTravelCost` `0`, `EnableMalaria` `1`,
    `VanillaColorgrading` `0`, `DarkerNights` `0`;
  - state for other features: `DiamondCounter`, `DiamondBriefcasesCollected`,
    `DLC1_Weapon1Purchased`..`3` (`1`), `GPSUpgradePurchased`, `EquippedMapGadget` (`1`),
    `FogOverride` (`""`), `MorningEnvironmentEnabled`, `NightEnvironmentEnabled`,
    `PhoneCallInstance`, `PrimaryBuddyEntityID` (`nil`), `ConvoyMissionsAccepted_W1`/`_W2`,
    `FinalConvoyMissionCompleted`, `FinalStoryMissionCompleted`, `BSQMissionsCompleted`,
    `BSQBuddyName` (`"x"`), `SkippedIntro`, `FinishedIntro`, `TutorialFinished` (all `0` unless
    noted).

A script that does arithmetic on one of these fields fails if the field is missing, so a feature
that uses one has to be picked with this page.

## Uncertain

- That the table is saved with the game, so the new fields exist only in a save started with the
  mod (one reason the mod asks for a new game), is an inference; the settings, at least, are
  re-read on every load.
- `AcceptedMissionID`, which several scripts still read, is a vanilla field.
