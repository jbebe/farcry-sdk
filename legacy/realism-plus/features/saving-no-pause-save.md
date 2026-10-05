---
title: No saving from the pause menu
kind: component
bundle: saving
status: located
systems: [save, ui, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
verified: re
---

# No saving from the pause menu

The pause menu's Save entry is gone, so a game can only be saved at in-game save points
(safehouses, gun shops, bus stops, mission givers). This is the engine half of the Limited Saving
choice. The Save Anywhere `Dunia.dll` leaves both strings intact, and the quicksave key is handled in
the controls files ([`saving-quicksave-unbound`](saving-quicksave-unbound.md)).

## How

- **`CSaveGamePage`** is the class name the save-game Magma page registers under (`FUN_107E8F80`,
  GOG). Zeroed, the page has no name to be created by.
- **`PAUSE_SAVEGAME`** is the pause menu's save entry, pushed by the pause page's `Setup`
  (`0x10847593`, GOG). Zeroed, the entry is built with an empty id.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| page class | `0x10EAAB24` | `0x10E2273C` | `CSaveGamePage` -> 13 zero bytes |
| menu entry | `0x10EB0B38` | `0x10E286C0` | `PAUSE_SAVEGAME` -> 14 zero bytes |

## Uncertain

- How the pause menu draws an entry with an empty id (hidden, or present but dead) was not traced.
- Whether the save-point dialogs reach the save page by this class name too has not been checked.
  The mod still saves at save points, so they presumably do not.
