---
title: Every killed soldier drops a grenade
kind: component
bundle: gameplay
status: located
systems: [economy, ai]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/GameplayManagerService/GameplayManagement/ChanceToDropGrenade@*"
exclude: []
requires: []
verified: diff
---

# Every killed soldier drops a grenade

`engine/gamemodes/gamemodesconfig.xml`, `GameplayManagerService` → `ChanceToDropGrenade`. The
chance per reputation tier is set to 1: `Experimented` 0.5, `Hardcore` 0.33 and `Infamous` 0.25 all
become 1. `Casual` was already 1. [Data recipes](../../../docs/docs/modding/data-recipes.md)
describes this block as the chance that a killed merc drops a grenade.
