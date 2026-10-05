---
title: Record playback key removed
kind: component
bundle: fixes
claims:
  - "Disabled buggy \"record playback\" feature"
status: located
systems: [input, engine]
match:
  - "config/defaultusercontrols.xml/category_misc.xml#Control[toggle_recording]"
  - "config/inputactionmapcommon.xml/common_system.xml#Binding[17]"
exclude: []
requires: []
verified: diff
---

# Record playback key removed

Numpad 8 no longer starts the engine's input recorder, the "record playback" of the claim, which
shows `Recording Playback...` on screen and writes demo files (see
`docs/docs/engine-internals/demo-recording.md`).

## How

- `config/inputactionmapcommon.xml`, action map `common_system`: the binding
  `kb:numpad8` / `press` / `toggle_recording` is removed.
- `config/defaultusercontrols.xml`, `CATEGORY_MISC`: the rebindable control `toggle_recording`
  (`key1="kb:numpad8"`, action map `common_system_remap`) is removed, so it cannot be bound again
  from the controls menu.

The recorder itself is left in the engine; only the signal that starts it is gone.

## Uncertain

- The mod's `Dunia.dll` patches are still being traced; one may disable the recorder too.
