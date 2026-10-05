---
title: Quieter reload prompt
kind: component
bundle: ui
claims: []
status: located
systems: [ui, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/HudService/CommonSingle/UI[CFCXMainHudUI]@reloadPromptAmmoRatio"
  - "languages/english/oasisstrings.fragment.xml#Hud/{RELOAD,UNJAM}"
exclude: []
requires: []
verified: diff
---

# Quieter reload prompt

The HUD's reload hint comes up later, when the magazine is nearly empty, and in English the "Reload"
and "Unjam" prompt words are gone. Not a line of the readme.

## How

- `engine/gamemodes/gamemodesconfig.xml`, `HudService/CommonSingle/UI[CFCXMainHudUI]`:
  `reloadPromptAmmoRatio` 0.25 -> 0.1, the share of a magazine left when the prompt appears.
- `languages/english/oasisstrings.fragment.xml`, `Hud/RELOAD` "Reload" and `Hud/UNJAM` "Unjam" ->
  empty. Other languages keep their words.

## Uncertain

- Whether an empty string leaves the prompt's key glyph on screen alone or hides the prompt
  entirely is not checked in game.
