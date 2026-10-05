---
title: Map and gadget key on Tab
kind: component
bundle: controls
claims:
  - "Map - Tab"
  - "Default Map/Gadget button is now Tab to prevent conflict with holster"
status: located
systems: [input]
match:
  - "config/defaultusercontrols.xml/category_weapons.xml#Control[nextgadget]@key1"
  - "config/inputactionmap{common,single}.xml/*.xml#*[*]@input"
exclude: []
requires: []
verified: diff
---

# Map and gadget key on Tab

The map (and the next-gadget cycle) moves from 5 to Tab.

## How

Every keyboard use of `kb:5` becomes `kb:tab`:

- `config/defaultusercontrols.xml`, `CATEGORY_WEAPONS`: `Control[nextgadget]@key1`, the rebindable
  default.
- `config/inputactionmapsingle.xml`: `ActionMap[weapons]` `Binding[1]` and `NoResend[1]`,
  `driving` `Binding[0]` and `NoResend[1]`, and `NoResend[1]` in `briefing`, `map_briefing` and
  `mapcompass`.
- `config/inputactionmapcommon.xml`: `common_briefinginteraction` `Binding[1]` and `common_driving`
  `Binding[9]`.

10 values. In multiplayer Tab shows the scoreboard (`inputactionmapmulti.xml`, unchanged).

## Depends on

Nothing. The readme gives the reason as a conflict with the holster key (`input-holster-key`), which
is N; the conflict was presumably with an earlier binding.

## Uncertain

- `player-phone-gadget-slot` puts the phone on the gadget cycle this key steps through; whether Tab
  then alternates map and phone is not checked.
