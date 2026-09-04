# UFCP — Unofficial Far Cry Patch

Bugs Ubisoft never patched, fixed in the running game instead of in the files on disk. Nothing is
overwritten, nothing survives uninstalling it, and it stacks with the data mods you already have.

Requires FCSE (the Far Cry Script Extender), the plugin loader, released from the same place as this
archive — and Far Cry 2 v1.03, either the Steam/Ubisoft Connect build or the GOG/retail one.

## Installing

1. Install FCSE: `FCSE.exe` goes next to the game's own `FarCry2.exe`, in `bin\`.
2. Copy `plugins\UFCP.dll` from this archive into the game's `bin\plugins\`.
3. Launch `FCSE.exe` instead of `FarCry2.exe`.

`bin\fcse.log` lists what was applied. To uninstall, delete the DLL.

## Fixes

Always applied. A fix that needs a switch is a preference in disguise.

- **Jackal tapes** — the same recording, usually *#09. Stealing Boots*, plays every time in the
  southern map instead of advancing through the set.
- **Predecessor tapes** — restores the seven Intel Bonus predecessor missions.
- **Machetes** — restores the Primitive and Homemade machete variants. Pick one in the game's own
  Options → Game → Machete Type.

The two restorations unlock content that ships inside the game's own files but is held behind an
ownership check that can no longer succeed: both were Ubisoft promotions that ended, and the service
and registry key they depended on are gone. Nothing anyone can still buy is bypassed.

## Options

In the Mod Configuration Menu, on the Options screen. All three leave the game exactly as it shipped
until you change them, and are saved in `bin\fcse.ini`.

- **Field of view** — 65 to 120 degrees, default 75, which is the game's own value and turns the
  feature off entirely. A change takes effect on the next load rather than instantly.
- **Processor affinity** — All cores (default), Physical cores only, 4 cores, or 1 core. Restricting
  the game to fewer processors is the long-standing workaround for the physics and timing artefacts
  the engine shows on machines far larger than anything it was tested on, such as NPCs visibly
  bouncing. It costs performance, which is why it is off by default.
- **Developer console** — off by default. Far Cry 2 has its own console on the `~` key, but most of
  its commands are marked developer-only and answer "Unknown command" even though they are there.
  Turning this on lists and runs them: loading a level, setting health, teleporting to the current
  objective, the AI debug view and about fifty more. Commands meant for multiplayer or the editor
  stay hidden, because that is a separate filter this does not touch.
