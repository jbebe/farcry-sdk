---
title: HUD fades sooner, no reload prompt
kind: component
bundle: gameplay
status: located
systems: [ui]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/HudService/**"
exclude: []
requires: []
verified: diff
---

# HUD fades sooner, no reload prompt

`engine/gamemodes/gamemodesconfig.xml`, `HudService`, for both `CommonSingle/UI[CFCXMainHudUI]` and
`CommonMulti/UI[CFCXMPHudUI]`:

- `fadeOutDelay` 3.0 → 0.3. By its name, the HUD hides 0.3 s after it was last needed instead of
  3 s.
- `reloadPromptAmmoRatio` 0.25 → 0. By its name, the reload prompt that showed once the magazine
  fell to a quarter now never shows.

Neither attribute has been traced or tried in game.
