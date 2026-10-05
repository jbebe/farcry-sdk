---
title: Faster HUD fade
kind: component
bundle: ui
claims:
  - "Faster HUD fade"
status: located
systems: [ui]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/HudService/CommonSingle/UI[CFCXMainHudUI]@fadeOutDelay"
exclude: []
requires: []
verified: diff
---

# Faster HUD fade

The health and ammo HUD fades out one second after it was last needed instead of three.

## How

`engine/gamemodes/gamemodesconfig.xml`, `GameModeProperties/HudService/CommonSingle`, the
single-player main HUD (`UI` `CFCXMainHudUI`): `fadeOutDelay` 3.0 -> 1.0.

The same element's `reloadPromptAmmoRatio` is `ui-no-reload-prompt`.

## Uncertain

- That the delay is in seconds and counts from the HUD's last update is inferred from the name.
