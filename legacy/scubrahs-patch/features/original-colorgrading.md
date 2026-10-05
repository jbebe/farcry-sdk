---
title: Neutral colour grading
kind: component
bundle: visuals
claims:
  - "Restored original colorgrading without color filters (thanks miru)"
status: located
systems: [graphics, environment]
match:
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[*]/Template[*]/{fColorRemapRed,fColorRemapGreen,fColorRemapBlue,fContrast,fSaturation}"
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[*]/Template[*]/curveMinimumLuminance/**"
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[10]/{text_NameId,NameId,text_Template,Template[0]}"
  - "worlds/*/generated/world*.managers.fcb/databaseitemmanager.*#Templates/Template[+12]"
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{1717,2017}"
exclude: []
requires: []
existing: mods/sky-overhaul — src/grade.cpp (replaces the weather presets' grade)
verified: diff
---

# Neutral colour grading

The brown-orange filter is gone: every weather's grade has no per-channel colour remap, no extra
contrast and full saturation. A setting in `ScubrahsPatch.lua` brings the game's own grade back.

## How

Both worlds' `world*.managers.fcb`, `DataBaseItemManager`, the `CEnvironmentAdaptiveBloom` presets:

- `Default.Clear`, `Default.Desert`, `Default.Default` and `Default.ScriptedEvent.AdaptiveBloom`:
  `fColorRemapRed`, `fColorRemapGreen`, `fColorRemapBlue` and `fContrast` -> 0, `fSaturation` -> 1
  (base game e.g. Clear 0.1 / -0.06 / -0.5, contrast 0.1, saturation 0.5; Default contrast 0.6,
  saturation 0.45). `Default.Storm.AdaptiveBloom` the same, its contrast already 0.
- `Default.Jungle.AdaptiveBloom` is rebuilt the same way. The change list shows this as the base
  game's jungle preset at index 10 being renamed and a new one added at `[+12]`.
- **`Default.Original.AdaptiveBloom`** (new, at index 10): a copy of the base game's
  `Default.Clear.AdaptiveBloom` grade (remap 0.1 / -0.06 / -0.5, contrast 0.1, saturation 0.5),
  under a new GUID `{0C5D3500-930E-44A3-A647-2AF20F31BADC}`.

Bloom, adaptation and threshold values are untouched.

**The vanilla-grade option.** When `VanillaColorgrading` is 1, `graphics-environment-manager` holds
the adaptive-bloom override `Default.Original.AdaptiveBloom`. Two hunks of the town escape mission,
`domino/user/a1sm01_townescape.a1sm01_mission.lua` at base lines 1717 and 2017, skip the mission's
own set and removal of the `Default.Desert.AdaptiveBloom` override while that option is on, so the
mission does not displace it.

## Depends on

- The neutral grade needs only the presets. The vanilla-grade option also needs
  `graphics-environment-manager` and the `VanillaColorgrading` global
  (`domino/user/master_gameglobals.globals.lua`, set from `ScubrahsPatch.lua`, both claimed
  elsewhere). It is not in `requires` because the manager drags in the morning fog, night and
  map-gadget logic; without the global the two mission hunks fall through to the base game's
  behaviour.
