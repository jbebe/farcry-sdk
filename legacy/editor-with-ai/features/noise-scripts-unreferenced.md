---
title: Scripts nothing loads
kind: noise
systems: [engine]
match:
  - "_hash/120a6c66.bin"
  - "_hash/625f4f55.bin"
exclude: []
verified: diff
---

# Scripts nothing loads

Two more Lua boxes in the same style as [`scripts-community-samples`](scripts-community-samples.md).
Nothing in the mod names them, and neither does a vanilla file.

- `_hash/120a6c66.bin`: an `Init` that calls `EnableScriptedAIMode()`.
- `_hash/625f4f55.bin`: an `Init` that spawns `Custom.RaZoR_Object.Natural.Cactus1` at the map
  centre. The file opens with `export = {},`, a trailing comma, so it would not even load.

No `fileBoxPath` in the mod's libraries hashes to either name, and neither hash appears as a
string or as raw bytes anywhere in the mod. Their real paths are unknown. A community map that
places an entity pointing at them would run them; on their own they do nothing.
