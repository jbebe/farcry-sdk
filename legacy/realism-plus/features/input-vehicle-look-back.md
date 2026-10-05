---
title: Look back from a vehicle (V)
kind: component
bundle: gameplay
status: located
systems: [input, vehicles]
match:
  - "config/defaultusercontrols.xml/category_vehicles.xml#Control[lookback]"
  - "config/inputactionmapcommon.xml/common_in_vehicle.xml#{Import[+3],Binding[+12],Binding[+13],CompoundInput[look_pov][+1]}"
  - "languages/*/oasisstrings.fragment.xml#Actions/lookback"
exclude: []
requires: []
verified: diff
---

# Look back from a vehicle (V)

While driving, holding V turns the view to look behind, as the mouse and gamepad already could. The
key is rebindable as "Look Back".

## How

- `config/defaultusercontrols.xml`, `CATEGORY_VEHICLES`: new `Control[lookback]`, `key1` `kb:v`,
  actionmap `common_lookback_remap`, group 2, conflict mask 12.
- `config/inputactionmapcommon.xml`, `ActionMap[common_in_vehicle]`: imports
  `common_lookback_remap` (optional), adds a keyboard `CompoundInput` `look_pov` (`v` on axis 1,
  inverted), and binds `kb:look_pov` press and release to the signal `look_pov`. The base game
  already has `look_pov` compound inputs for the mouse and the pad in this map.
- `Actions/lookback`, the control's label, in all ten languages ("Look Back", "Regarde en
  arrière"...).

## Uncertain

- That axis 1 inverted is "look behind" is read from the pad's existing `look_pov`; not checked in
  game.
