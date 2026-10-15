# Legacy Plugin

Preference tweaks from Scubrah's Patch, Realism Plus and FC2JackalFix, switched on and off in game
instead of by installing a patched `Dunia.dll`. Nothing is overwritten and nothing survives
uninstalling it. Those mods' own data files still come from the mods themselves; this plugin
supplies only the engine side.

Requires FCSE (the Far Cry Script Extender) 1.4.0 or later, the plugin loader, released from the
same place as this archive, and Far Cry 2 v1.03, either the Steam/Ubisoft Connect build or the
GOG/retail one.

## Installing

1. Install FCSE: `FCSE.exe` goes next to the game's own `FarCry2.exe`, in `bin\`.
2. Copy `plugins\LegacyPlugin.dll` from this archive into the game's `bin\plugins\`.
3. Launch `FCSE.exe` instead of `FarCry2.exe`.

`bin\fcse.log` lists what was applied. To uninstall, delete the DLL.

## Options

In the Mod Configuration Menu, on the Options screen, saved in `bin\fcse.ini`. Every row leaves the
game exactly as it shipped until you change it.

- **Briefings keep control** — an NPC's briefing no longer stands you up and takes control away.
- **Keep weapon on rescue** — a buddy rescue no longer swaps your secondary weapon for a pistol.
- **No forced buddy rescues (new game)** — the factions stop assigning buddy rescue missions, so a
  bugged one cannot block the story. Start a new game after changing it.
- **No reputation drop** — finishing A1SM03 or A2SM08 no longer lowers your reputation.
- **Hide diamond message** — picking up diamonds no longer shows the game's own message, for mods
  that show their own.
- **No blinking pickups** — weapons, ammo, health, diamond cases, beds and other usable things stop
  flashing.
- **No save icons on map** — save points lose their icon on the map and the GPS.
- **Limited saving** — Save Game leaves the pause menu and F5/F9 stop working; save points and
  autosaves still save.
- **Brown filter** — on by default, the game's own colour grade. Off gives a neutral grade without
  the brown-orange tint.
- **No hit indicator** — no directional indicators when you take damage.
- **HUD fade-out** — how long the health, ammo and equipment counters stay up: always hidden, 1 to
  60 seconds, or always shown. 3 seconds is the game's own.
- **Skip tutorial pop-ups** — off, the pop-ups that interrupt play, or those and the floating hints.
- **Aim toggle** — tap aim to stay in the sights, tap again to lower them.
- **Sprint toggle** — tap sprint to keep running until you tap again or stop.
- **Sprint turn** — sprinting no longer slows how fast you turn.
- **No rim lighting** — removes the glow outlining characters, mostly seen at night.
- **No sprint and aim blur** — removes the screen blur while sprinting and aiming down the sights.

## Credits

The tweaks come from **Scubrah's Patch** by scubrah, **Realism Plus** by Boggalog, and
**FC2JackalFix** (https://github.com/TGP482/FC2JackalFix) by Joshhhuaaa and TGP482, whose rim
lighting work credits Boggalog's **Far Cry 2 Patched** and whose blinking items work credits
**FoxAhead's Far Cry 2 Multi Fixer**. The FC2JackalFix code is covered by its licence:

```
MIT License

Copyright (c) 2026 Joshhhuaaa, TGP482

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
