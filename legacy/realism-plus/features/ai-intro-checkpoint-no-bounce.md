---
title: No rigid-body physics for the opening checkpoint's soldiers
kind: component
bundle: fixes
status: located
systems: [ai]
match:
  - "levels/*/generated/worldsectors/*.data.fcb/*#Components/CCharacterPhysComponent/CharacterParams/bUseRigidBased"
exclude: []
requires: []
verified: diff
---

# No rigid-body physics for the opening checkpoint's soldiers

Nine soldiers at the opening taxi ride's checkpoint - the scripted mercs who stop the taxi and the
guards posted beside them - no longer use rigid-body character physics, the setting blamed for NPCs
bouncing in place at high frame rates.

## How

In `levels/w1_d_3` sector 2438, nine placed soldiers already carry an instance
`CCharacterPhysComponent/CharacterParams` (with only `fMaxSlope`); each gains
`bUseRigidBased` `False`:

- the opening's scripted mercs, layer `missions\openingsequence\taxiride`: `Intro_IntimidatingMerc`,
  `MissionSpecific.Intro_AngryMerc_0`, `Intro_SuspiciousMerc_0`, `GenericScriptedSoldier_5`;
- the checkpoint's guards `Blue_Faction.Assault_Caucasian_43`, `Assault_Nubian_6`,
  `ShotgunMan_Caucasian_3`, `ShotgunMan_Nubian_2`, `Sniper_Caucasian_1` (also moved into the
  Functional Outposts layer `missions\outposts\w1_d_3\gbiimii` by `outposts-functional`).

Nothing else in the mod sets the flag: every soldier and buddy archetype, in the world libraries and
in the mod's override copies, keeps `True`.

## Compared with Scubrah's Patch

[`bouncing-npcs`](../../scubrahs-patch/features/bouncing-npcs.md) sets the flag to `False` on every
human archetype, so the fix reaches every soldier, buddy and civilian. This mod fixes only the scene
the player watches from the taxi without being able to move.

## Uncertain

- If the published bouncing-NPC fix reaches the rest of the world, it is not through data; whether
  the mod's `Dunia.dll` does it is for the engine pages.
- That an instance value overrides the archetype's per field is inferred from the instance carrying
  only the fields it changes.
