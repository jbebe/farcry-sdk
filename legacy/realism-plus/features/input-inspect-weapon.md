---
title: Hold reload to inspect the weapon
kind: component
bundle: gameplay
status: located
systems: [input, weapons]
match:
  - "config/inputactionmapcommon.xml/common_weapons.xml#{Binding[+8],Binding[+24]}"
  - "languages/*/oasisstrings.fragment.xml#Actions/reload"
exclude: []
requires: []
verified: diff
---

# Hold reload to inspect the weapon

Holding R (X on a gamepad) plays the idle "look at the gun" animation on demand.

## How

- `config/inputactionmapcommon.xml`, `ActionMap[common_weapons]`: `kb:r` hold and `pad:x` hold ->
  signal `cyclebreaker`. That signal starts the base game's idle cycle breaker,
  `Main Avatar/Common/IdleCycleBreaker`, which the engine otherwise sends after a spell of idling.
- `Actions/reload`, the reload control's label, gains the third use in all ten languages:
  "Reload / Unjam" -> "Reload / Unjam / Inspect" ("Recharger / Décoincer / Inspecter"...).

There is no separate control: the binding rides on the reload key, a tap reloading and a hold
inspecting.

`legacy/scubrahs-patch` `weapon-inspect` binds the same signal to its own key (I) and also edits the
state machine so the animation is not cut short; this mod does not.

## Uncertain

- With the base state machine, moving or aiming may abort the animation early, as an idle breaker
  does; not checked.
