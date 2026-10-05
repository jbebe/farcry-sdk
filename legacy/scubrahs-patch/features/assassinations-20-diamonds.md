---
title: Assassination missions pay 20 diamonds
kind: component
bundle: balancing
claims:
  - "All assassination missions now reward you with 20 diamonds on completion"
status: located
systems: [economy, missions]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/MissionManagerService/MissionManagement@AssassinationReward*"
exclude: []
requires: []
verified: diff
---

# Assassination missions pay 20 diamonds

Every assassination (cell tower) mission pays 20 diamonds in both acts.

## How

Two attributes of `MissionManagerService/MissionManagement` in `engine/gamemodes/gamemodesconfig.xml`:
`AssassinationRewardWorld1` `10 -> 20` and `AssassinationRewardWorld2` `15 -> 20`. The engine pays
the reward itself; no script is involved.

## Depends on

Nothing. `economy-diamond-counter` mirrors the same +20 (missions `ASSW1`/`ASSW2`) into its own
script-side count, but the reward does not need it.
