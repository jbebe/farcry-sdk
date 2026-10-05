---
title: Three inventory packs nothing uses
kind: noise
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{warlord_sniper,warlord_shotgun,buddy_sniper}]"
exclude: []
verified: diff
---

# Three inventory packs nothing uses

`engine/gamemodes/gamemodesconfig.xml` gains three whole `InventoryPacks` entries: `warlord_sniper`,
`warlord_shotgun` and `buddy_sniper`. An archetype picks its pack through `packInventoryPack`. No
archetype names these three, whether in the template world's library, world 1's, or the mod's own
custom library, which references only `buddy_shotgun`. They do nothing.
[The guide](../../../docs/docs/modding/guide/patrols.md) also reports that new packs cannot be
created at all.
