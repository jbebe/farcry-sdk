---
title: Upgrades page labelled as broken
kind: component
bundle: ui
status: located
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#PauseMenu/UPGRADES_*"
exclude: []
requires: [ui-upgrades-page-blank]
verified: diff
---

# Upgrades page labelled as broken

In English, the pause menu's Upgrades page tells the player it does not work and where to look
instead.

## How

`languages/english/oasisstrings.fragment.xml`, `PauseMenu`:

| Key | Base | Mod |
|---|---|---|
| `UPGRADES_PAGE_TITLE` | UPGRADES | THIS MENU IS BROKEN |
| `UPGRADES_PRIMARYWEAPON_CATEGORY`, `_SECONDARYWEAPON_`, `_SPECIALWEAPON_`, `_VEHICLES_`, `_EQUIPMENT_` | Primary Weapons, ... | BROKEN - LOOK RIGHT |
| `UPGRADES_AMMO_UPGRADES_TITLE` | Ammo Upgrades | BOUGHT UPGRADES DO WORK |
| `UPGRADES_RM_MANUAL_TITLE` | Repair Manual | TO SEE WHAT YOU HAVE |
| `UPGRADES_OP_MANUAL_TITLE` | Operations Manual | USE THE SHOP COMPUTER |

The three right-hand titles read as one sentence: "Bought upgrades do work / to see what you have /
use the shop computer". The other languages keep their titles.

## Depends on

- `ui-upgrades-page-blank`: the `Dunia.dll` patch that stops the page showing purchase states, which
  this notice explains. Without it the notice is wrong.
