# DevTools

The developer side of Far Cry 2, made reachable — for modders and reverse engineers rather than for
players. Nothing is overwritten, nothing survives uninstalling it, and it stacks with the mods you
already have.

Requires FCSE (the Far Cry Script Extender), the plugin loader, released from the same place as this
archive — and Far Cry 2 v1.03, either the Steam/Ubisoft Connect build or the GOG/retail one. UFCP,
the unofficial patch, is a separate plugin: install both, or either on its own.

## Installing

1. Install FCSE: `FCSE.exe` goes next to the game's own `FarCry2.exe`, in `bin\`.
2. Copy `plugins\DevTools.dll` from this archive into the game's `bin\plugins\`.
3. Launch `FCSE.exe` instead of `FarCry2.exe`.

`bin\fcse.log` lists what was applied. To uninstall, delete the DLL.

## Fixes

Always applied.

- **Savegame launch** — `FarCry2.exe -load <name>.sav` quit to desktop in a fraction of a second
  instead of booting straight into that save. It is the command line's own way to skip the menus,
  and the fastest way to test the same spot in the game over and over. The name needs its `.sav`
  extension.

## Options

In the Mod Configuration Menu, on the Options screen. Saved in `bin\fcse.ini`, and leaving the game
exactly as it shipped until you change it.

- **Developer console** — off by default. Far Cry 2 has its own console on the `~` key, but most of
  its commands are marked developer-only and answer "Unknown command" even though they are there.
  Turning this on lists and runs them: loading a level, setting health, teleporting to the current
  objective, the AI debug view and about fifty more. Commands meant for multiplayer or the editor
  stay hidden, because that is a separate filter this does not touch.
