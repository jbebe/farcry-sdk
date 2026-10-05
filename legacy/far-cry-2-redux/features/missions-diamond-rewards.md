---
title: Diamond rewards for buddy side quests and buddy assassinations (inert)
kind: component
bundle: missions
claims:
  - "Mod updated to fix the amount of diamonds received from missions."
  - "Changed some diamond rewards"
status: located
systems: [economy, missions]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/MissionManagement@AssassinationRewardBuddy"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/MissionManagement/SideMissions/BSQMissions/Item[*]@diamondreward"
exclude: []
requires: []
verified: re
---

# Diamond rewards for buddy side quests and buddy assassinations (inert)

The only mission-reward data in the mod. Neither change pays out anything in game: the engine never
reads the one, and nothing in the game asks for the other. Story, faction (library) and
cell-tower assassination rewards are left at their vanilla values.

## How

`engine/gamemodes/gamemodesconfig.xml`, `MissionManagerService/MissionManagement`, 13 values:

- `SideMissions/BSQMissions`: the twelve act 2 buddy side quests (`GuinaAssassination`,
  `FromAfar`, `APumpingExplosion`, `PassportToHell`, `Wanted`, `DirtyTalk`, `WipeTheCompetition`,
  `TreasureHunt`, `SchoolsOut`, `Driveby`, `Jack_in_a_box`, `LittleConspiracy`) gain
  `diamondreward="5"`; vanilla gives these items no reward attribute, and the act 1 quests are left
  without one.
- `AssassinationRewardBuddy` `20` -> `15`.

## Why nothing changes

Traced in `FarCry2_server` (the symbol build of the engine):

- `DiamondReward` is a member of `CStoryMission` and `CLibraryMission` only
  (`RegisterProperties`). `CFCXMissionManager::GiveMissionReward`, the one place that pays a
  mission's diamonds (it is `CEconomyComponent::AddDiamond`'s only mission caller), looks the
  mission up among `ASSW1`, `ASSW2`, `ASSBUDDY`, the library missions and the story missions. A
  buddy side quest is none of these, so its `diamondreward` is never read.
- `AssassinationRewardBuddy` is what `GiveMissionReward` pays for the tag `ASSBUDDY`. No script or
  data file in the base game or the mod passes that tag (the cell-tower missions pay `ASSW1` and
  `ASSW2`), so the value is never used.

Buddy side quests pay diamonds only through a script calling `AddDiamonds`, as Scubrah's Patch does
in its `MissionCompleted` box
([`buddy-missions-20-diamonds`](../../scubrahs-patch/features/buddy-missions-20-diamonds.md)). The
working reward recipes are in Boggalog's guide
([mission rewards](../../../docs/docs/modding/guide/mission-rewards.md)).

## Uncertain

- Both readme lines are matched here because this is the only reward data in the mod. If the
  "fix" was a return of the story and library rewards to vanilla after an earlier Redux version
  raised them, that is consistent with this version's data but cannot be seen from it.
- The trace is in the server build; that the retail `Dunia.dll` reads rewards the same way is
  assumed.
