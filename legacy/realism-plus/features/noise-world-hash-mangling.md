---
title: Prefab and fire-effect name hashes mangled by the mod's converter
kind: noise
status: located
systems: [world]
match:
  - "worlds/*/generated/world*.managers.fcb/prefabmanager.*#PrefabDescriptions/Description[{005DC34B,4BEFBFBD5D00}]"
  - "worlds/*/generated/world*.managers.fcb/firemanager.*#hidStimEffects/StimEffects[{00C21052,5210EFBFBD00}]"
exclude: []
requires: []
verified: diff
---

# Prefab and fire-effect name hashes mangled by the mod's converter

Not an edit, 8 changes: in both worlds' `world*.managers.fcb`, two name hashes went through a text
round trip that replaced a byte of `0x80` or above with `EF BF BD`, so each reads as one entry
removed and one added:

- `PrefabManager`, the description `DogonShelter_03`: `Name` `005DC34B` -> `4BEFBFBD5D00`.
- `FireManager`, the stim effect of `Burnable.Object.StrawRoof01`: `00C21052` -> `5210EFBFBD00`.

The same two values come out of Scubrah's Patch's tooling
([`noise-graphics-prefab-name`](../../scubrahs-patch/features/noise-graphics-prefab-name.md),
[`noise-player-hash-mangling`](../../scubrahs-patch/features/noise-player-hash-mangling.md)), which
suggests the same converter. `DogonShelter_03` is a multiplayer map prefab; the straw roof's burn
effect may lose its lookup, as noted there.
