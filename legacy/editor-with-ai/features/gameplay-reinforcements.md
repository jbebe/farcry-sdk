---
title: More kinds of reinforcement
kind: component
bundle: gameplay
status: partial
systems: [ai, patrols]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/ReinforcementArchetypes/**"
exclude: []
requires: []
verified: diff
---

# More kinds of reinforcement

The reinforcement list grows from one soldier archetype to a set of roles per faction. Most of the
new names point at nothing.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService` → `ReinforcementArchetypes`.
Vanilla lists `enemy_archetypes.Red_Faction.Assault_Caucasian` twice, once `type="redmerc"` and
once `type="BlueMerc"`, plus `vehicle.Land.Rover` as `type="vehicle"`. The mod removes the
`redmerc` entry and adds eleven entries. They are `Blue_Faction` and `Red_Faction` versions of
`Assault_Caucasian` (Blue only, since Red is kept), `Shotgun_Caucasian`,
`RocketLauncher_Caucasian`, `Mortar_Caucasian`, `Sniper_Caucasian` and `Warlord_Caucasian`. Every
`Blue_Faction` entry is typed `Redmerc` and every `Red_Faction` entry `BlueMerc`.

Only `Assault_Caucasian` exists in both factions' libraries. `Sniper_Caucasian` is declared only in
the template world's library. `Shotgun_`, `RocketLauncher_`, `Mortar_` and `Warlord_Caucasian`
appear in no game file: the real archetypes are `ShotgunMan_`, `RocketMan_` and `MortarMan_`, and
there is no warlord soldier. A reinforcement drawn from those entries cannot spawn.

`status: partial` because, of the six new kinds per faction, only the assault soldier (and the
sniper in an editor map) is real.

## Uncertain

- Whether `type` names the faction the reinforcement fights for, which would mean the crossed
  `Redmerc`/`BlueMerc` sends the other faction's soldiers, or the faction that asks for help. Not
  traced.
- How the manager picks among several entries of one type (first, random), and what it does with
  an archetype it cannot spawn. Not traced.
