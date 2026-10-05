---
title: Higher story and faction mission rewards
kind: component
claims: []
status: located
systems: [economy, missions]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/MissionManagement/{StoryMission,LibraryMission}/Item[*]@diamondreward"
exclude: []
requires: []
verified: diff
---

# Higher story and faction mission rewards

Every story and faction (library) mission that pays diamonds pays 5 more. The published list does not
mention it; it is the income side of the re-balanced shop prices.

## How

`diamondreward` on 16 `MissionManagerService/MissionManagement` items in
`engine/gamemodes/gamemodesconfig.xml`, each `+5`:

- `StoryMission`: `FoolsErrand`, `HornetsNest` `20 -> 25`; `HouseClean1`, `HouseClean2` `25 -> 30`
- `LibraryMission`: `CopKiller`, `ReapSew`, `JunkyardDog`, `DirectSpear`, `GrowOp`, `OeduardRex`
  `15 -> 20`; `RadioArmageddon`, `PipeDreams`, `DentalPlan`, `FlyingJackal`, `BridgeTooFar`,
  `BunkerBuster` `30 -> 35`

The engine pays them; no script is involved.

## Depends on

Nothing. `economy-diamond-counter` mirrors these values into its own count.
