---
title: Enemy copies in the override library
kind: shared
status: located
systems: [ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/**.xml"
exclude:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{corpses,partnermissions}/*.xml"
requires: []
verified: diff
---

# Enemy copies in the override library

A shared piece: the mod copies 21 soldier archetypes into
`generated/entitylibrarypatchoverride.fcb`, where the base game has none, so their values win over
the world libraries. They are the only place the AI sensory changes exist.

## How

Whole new units: the Blue and Red faction `Assault`, `LightMachineGunner`, `ShotgunMan` (each
`_Caucasian` and `_Nubian`), the Blue `RocketMan`, `Sniper` and `MortarMan` pairs, Blue
`CarlGustaf_Nubian`, Red `RocketMan_Caucasian` and `Missions.Assassination_Target`.

Against the world libraries each carries:

- `CCharacterPhysComponent/CharacterParams/bUseRigidBased` `True` -> `False` - `bouncing-npcs`
- `CFCXAIComponent/AIObject/CPawnAgent/SensorySystem/FOVParameters/FOVMultipliers`:
  - `fNightTimeMultiplier` `0.5` -> `0.6` - `ai-night-visibility`
  - `fPlayerInVehicleMultiplier` `2` -> `1.7` - `ai-stealth-senses`
  - in the 12 riflemen, machine gunners and shotgunners only: `fPreCombatMultiplier` `0.75` ->
    `0.6`, `fCombatMultiplier` `1` -> `0.9`, `fPostCombatMultiplier` `1.25` -> `0.8` -
    `ai-stealth-senses`
- `CharacterParams/fMaxSlope` `60.000004` -> `60`, the editor's float rounding

The mod's world-library edits of the same soldiers change only `bUseRigidBased`, so without these
copies the sensory values stay as shipped. Six corpse archetypes copied alongside change nothing
(`noise-player-identical-copies`).
