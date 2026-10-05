---
title: Assassination targets can only be killed by the player
kind: component
bundle: gameplay
status: located
systems: [ai, missions]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/missions/assassination_target.xml#**/bIsInvincibleExceptToPlayer"
exclude: []
requires: []
verified: diff
---

# Assassination targets can only be killed by the player

An assassination mission's target no longer dies to anyone but the player.

## How

`enemy_archetypes.Missions.Assassination_Target` (copied into
`generated/entitylibrarypatchoverride.fcb`, redeclaring the world library's):
`CFCXCountersComponentAI/bIsInvincibleExceptToPlayer` `False` -> `True`. The flag is described in
Boggalog's guide ([buddies: invincibility](../../../docs/docs/modding/guide/buddies.md#invincibility)).

The same copy gets ordinary sight multipliers (`ai-stealth-precombat`). The archetype is the third
passenger of the `GhostPatrols.Convoy.AssassinationTarget` convoy jeep in both worlds, whose other
two seats `patrols-blue-crews` recrews.

## Uncertain

- Why is not stated. With `patrols-red-faction-crews` sending crews of the other faction through
  the map, a target could otherwise be killed by a passing patrol before the player reaches him;
  that this is the reason is an inference.
- Assassination targets placed in sectors may carry their own counters component; whether every
  target inherits the archetype's flag is not checked.
