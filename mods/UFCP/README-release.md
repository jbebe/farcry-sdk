# UFCP — Unofficial Far Cry Patch

Bugs Ubisoft never patched, fixed in the running game instead of in the files on disk. Nothing is
overwritten, nothing survives uninstalling it, and it stacks with the data mods you already have.

Requires FCSE (the Far Cry Script Extender) 1.4.0 or later, the plugin loader, released from the
same place as this archive — and Far Cry 2 v1.03, either the Steam/Ubisoft Connect build or the
GOG/retail one. DevTools, a separate plugin from the same project, is for working on the game rather
than playing it: it holds the developer console, booting straight into a save with `-load`, and
skipping the intro movies and title screen.

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
- **Exit crash** — quitting through Exit Game faulted instead of closing cleanly.
- **Mouse speed cap** — a fast flick turned the view less far than a slow one, because the gamepad's
  output ceiling was applied to the mouse as well.
- **High-precision timer** — loading screens ran far below the 30 FPS they are paced for.
- **Grass under spot lights** — headlights and flashlights left grass black however close it was.
- **CPU and GPU utilisation** — the job pool was sized for two-core machines, idle workers spun in
  the queue's critical section, the GPU could never run a frame ahead, and with a frame cap set the
  limiter burned a whole core busy-waiting for its deadline.

The two restorations unlock content that ships inside the game's own files but is held behind an
ownership check that can no longer succeed: both were Ubisoft promotions that ended, and the service
and registry key they depended on are gone. Nothing anyone can still buy is bypassed.

## Options

In the Mod Configuration Menu, on the Options screen, saved in `bin\fcse.ini`. All but one leave
the game exactly as it shipped until you change them — the frame rate is capped at 60 out of the
box, one row away from the stock behaviour.

- **Field of view** — 65 to 140 degrees, default 75, the game's own. The view on foot, cutscenes
  and ladders included; the weapon and arms, the sights, scopes and vehicles keep their own, and an
  arms dealer's computer is seen at the game's own view.
- **Maximum frame rate** — the engine's own limiter, capped at 60 by default. Takes effect without
  a restart.
- **Display mode** — borderless, applied on the next launch.
- **Processor affinity** — All cores (default), Physical cores only, 4 cores, or 1 core. Restricting
  the game to fewer processors is the long-standing workaround for the physics and timing artefacts
  the engine shows on machines far larger than anything it was tested on, such as NPCs visibly
  bouncing. It costs performance, which is why it is off by default.

## Credits

Much of the input, field-of-view, frame-rate and utilisation work is ported from **FC2JackalFix**
(https://github.com/TGP482/FC2JackalFix) by Joshhhuaaa and TGP482, whose field-of-view work in turn
credits **FoxAhead's Far Cry 2 Multi Fixer**. That code is covered by FC2JackalFix's licence:

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
