---
title: Collection manager - editor-only burn profile names rewritten
kind: noise
systems: [world]
match:
  - "worlds/tmpla/generated/tmpla.managers.fcb/**"
exclude: []
verified: diff
---

# Collection manager - editor-only burn profile names rewritten

13 changes to the template world's collection manager
(`worlds/tmpla/generated/tmpla.managers.fcb`, `collectionmanager.2054037061454791958`) that do
nothing in game.

Each sets `text_BurnProfileMP` on one collection from the empty GUID
`{00000000-0000-0000-0000-000000000000}` to `{5110E19C-6EB8-44C6-B368-583F26445DE1}`. The
collections are vanilla collections 69 to 79, 142 and 143, mostly the "DoNotUse" grasses
(`DoNotUse`, `DoNotUse_SavGrassAShort`, `DoNotUse_SavGrassB`, ..., `OldGrasscompare`,
`FCX_BushSavannah_MP`).

A `text_` member is the editor's plain-text twin of the hashed member beside it, here `BurnProfileMP`.
The engine never reads twins (see the [FCB notes](../../../docs/docs/file-formats/fcb.md)), and the
hashed `BurnProfileMP` of these 13 stays `900C42F5`. Collections that really use the
`{5110E19C-…}` profile, such as `FCX_Airstrip01`, carry `6FE037E6` there, so the new twins no
longer even match their hashes. The change is most likely a side effect of the
tool that re-exported the manager, filling in a twin that vanilla left blank.

The collection palette that exposes these collections is
[`editor-collections`](editor-collections.md).
