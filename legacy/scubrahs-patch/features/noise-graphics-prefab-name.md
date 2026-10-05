---
title: Corrupted prefab name hash
kind: noise
claims: []
systems: [world]
match:
  - "worlds/*/generated/world*.managers.fcb/prefabmanager.*#PrefabDescriptions/Description[*]/Name{,@type}"
exclude: []
requires: []
verified: diff
---

# Corrupted prefab name hash

A re-encoding accident of the mod's tooling, not an edit.

Both worlds' `PrefabManager`, the first prefab description, `DogonShelter_03`: its `Name` hash
`005DC34B` is written back as the six bytes `4BEFBFBD5D00` (type `Hash` -> `BinHex`). The original
little-endian bytes `4B C3 5D 00` went through a text decode, and the non-ASCII `C3` became the
UTF-8 replacement character `EF BF BD`. The `text_Name` twin is unchanged.

The same pattern appears on other hash fields the mod's tool touched (multiplayer game modes and
the Jeep Liberty's part names in `worlds/tmpla`). In game the description's name no longer matches
its hash; `DogonShelter_03` is a multiplayer map's prefab, so nothing in the campaign is expected to
look it up.
