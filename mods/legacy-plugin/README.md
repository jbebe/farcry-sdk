# Legacy Plugin

The `Dunia.dll` half of the preference tweaks that community mods apply by shipping a patched
`Dunia.dll` or a code hook, as settings you can switch in game.

Legacy Plugin is an [FCSE](../../tools/FCSE) plugin: it patches `Dunia.dll` in memory at startup, so
it overwrites nothing and leaves no trace when uninstalled. Scubrah's Patch, Realism Plus and
FC2JackalFix already ship their own `patch.dat` data; this supplies only the engine side, so their
data can be installed without their `Dunia.dll`. It holds no fixes - those are [UFCP](../UFCP) - and
no data layer.

## The contract

- One tweak per file in `src/options/`, each exporting exactly two free functions:
  `void Install<X>()`, which finds its sites and installs any hooks, and
  `void __cdecl On<X>Changed(const FCSE_SettingValue* value, void* userdata)`.
- `src/main.cpp` declares both, calls every `Install<X>()`, then registers every row in one
  `RegisterSettings("LegacyPlugin", ...)` call. Rows are saved under `[LegacyPlugin]` in
  `bin\fcse.ini`.
- Every row defaults to the game as shipped.
- A tweak that cannot find its site logs it to `bin\fcse.log` and does nothing.

## Tweaks

In menu order.

| Row | Type | Default | File | Source | Status |
|---|---|---|---|---|---|
| Briefings keep control | checkbox | off | `briefing_control.cpp` | Scubrah's Patch, [skippable-dialog](../../legacy/scubrahs-patch/features/skippable-dialog.md) | built, not checked in game |
| Keep weapon on rescue | checkbox | off | `rescue_keeps_weapon.cpp` | Scubrah's Patch, [buddy-rescue-fluid](../../legacy/scubrahs-patch/features/buddy-rescue-fluid.md) | built, not checked in game |
| No forced buddy rescues | checkbox | off | `no_forced_rescue.cpp` | Scubrah's Patch, [buddy-rescue-optional](../../legacy/scubrahs-patch/features/buddy-rescue-optional.md) | built, not checked in game |
| No reputation drop | checkbox | off | `reputation_kept.cpp` | Scubrah's Patch, [reputation-kept](../../legacy/scubrahs-patch/features/reputation-kept.md) | built, not checked in game |
| Hide diamond message | checkbox | off | `no_diamond_message.cpp` | Scubrah's Patch, [economy-briefcase-tracker](../../legacy/scubrahs-patch/features/economy-briefcase-tracker.md) | built, not checked in game |
| No blinking pickups | checkbox | off | `no_blinking_items.cpp` | Realism Plus, [graphics-no-flashing-items](../../legacy/realism-plus/features/graphics-no-flashing-items.md); FC2JackalFix `gameplay/blinkingitems.ixx` | built, not checked in game |
| No save icons on map | checkbox | off | `no_save_icons.cpp` | Realism Plus, [nav-no-save-icons](../../legacy/realism-plus/features/nav-no-save-icons.md) | built, not checked in game |
| Limited saving | checkbox | off | `limited_saving.cpp` | Realism Plus, [saving-no-pause-save](../../legacy/realism-plus/features/saving-no-pause-save.md); FC2JackalFix `gameplay/limitedsaving.ixx` | built, not checked in game |
| Brown filter | checkbox | **on** | `brown_filter.cpp` | Scubrah's Patch, [original-colorgrading](../../legacy/scubrahs-patch/features/original-colorgrading.md); FC2JackalFix `graphics/saturation.ixx` | built, not checked in game |
| No hit indicator | checkbox | off | `no_hit_indicator.cpp` | FC2JackalFix `gameplay/hitindicator.ixx` | built, not checked in game |
| HUD fade-out | Always hidden · 1–60 s · Always shown | 3 s (game default) | `hud_fade.cpp` | FC2JackalFix `gameplay/hudfadeout.ixx` | built, not checked in game |
| Skip tutorial pop-ups | Off · Pop-ups · Pop-ups and hints | Off | `skip_tutorials.cpp` | FC2JackalFix `gameplay/tutorialmessages.ixx` | built, not checked in game |
| Aim toggle | checkbox | off | `aim_toggle.cpp` | FC2JackalFix `input/inputtoggles.ixx` | built, not checked in game |
| Sprint toggle | checkbox | off | `sprint_toggle.cpp` | FC2JackalFix `input/inputtoggles.ixx` | built, not checked in game |
| Sprint turn | checkbox | off | `sprint_turn.cpp` | FC2JackalFix `input/looksensitivity.ixx` | built, not checked in game |
| No rim lighting | checkbox | off | `no_rim_light.cpp` | FC2JackalFix `graphics/rimlighting.ixx` | built, not checked in game |
| No sprint and aim blur | checkbox | off | `no_blur.cpp` | FC2JackalFix `graphics/postfx.ixx` | built, not checked in game |

The FC2JackalFix modules are under [`tools/third-party/FC2JackalFix/source/`](../../tools/third-party/FC2JackalFix/source).

## Credits and licences

The tweaks are scubrah's ([Scubrah's Patch](https://www.nexusmods.com/farcry2/mods/328)),
Boggalog's ([Realism Plus](https://www.nexusmods.com/farcry2/mods/292)), and Joshhhuaaa and TGP482's
([FC2JackalFix](../../tools/third-party/FC2JackalFix)). FC2JackalFix's rim lighting credits
Boggalog's Far Cry 2 Patched, and its blinking items FoxAhead's
[Far Cry 2 Multi Fixer](https://github.com/FoxAhead/Far-Cry-2-Multi-Fixer). Each file names its
source.

Files ported from FC2JackalFix are covered by its licence:

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

## Building

```
.\build.ps1
.\build.ps1 -Config debug
.\build.ps1 -Install "C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin"
```

Needs the same x86 MSVC toolchain as FCSE; the whole dependency is
`tools/FCSE/include/fcse_api.h`. `.\verify_build.ps1 [-Config debug]` checks that the built DLL is
x86, links the CRT statically and exports `FCSE_Load`. Both run on every push and pull request
touching `mods/legacy-plugin` or the plugin ABI header (`.github/workflows/legacy-plugin-ci.yml`).
`python ..\..\scripts\verify_patterns.py` checks every byte pattern against both shipped
`Dunia.dll` builds; it needs the game, so it runs locally.

## Installing

1. Install FCSE (`FCSE.exe` next to the game's `FarCry2.exe`).
2. Drop `LegacyPlugin.dll` into `bin\plugins\`.
3. Launch `FCSE.exe`.
