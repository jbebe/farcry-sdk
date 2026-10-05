---
title: One syringe at most
kind: component
bundle: gameplay
status: located
systems: [player, economy]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[player]/**"
exclude: []
requires: []
verified: diff
---

# One syringe at most

`engine/gamemodes/gamemodesconfig.xml`, the `player` inventory pack: the four
`MaxAmmo[syringe]` entries go from `max` 6, 5, 4 and 3 to 1. The player can carry one healing
syringe at a time instead of three to six. The four entries are presumably the per-difficulty caps.
The bandolier upgrades that raise carrying caps are in `BonusService`
([`gameplay-upgrades`](gameplay-upgrades.md)).
