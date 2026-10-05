---
title: Inspect key (I)
kind: component
bundle: controls
claims:
  - "Inspect - I"
  - "Added a manual inspection weapon function"
  - "Added key rebinds for holster and inspect in the Options menu"
status: located
systems: [input, weapons]
match:
  - "config/defaultusercontrols.xml/category_actions.xml#Control[cyclebreaker]"
  - "config/inputactionmapcommon.xml/common_move.xml#Binding[+5]"
exclude: []
requires: [strings-control-labels]
verified: diff
---

# Inspect key (I)

Pressing I plays the idle "look at the gun" animation on demand; the key can be rebound in the
Options menu as "Inspect".

## How

- `config/inputactionmapcommon.xml`, `ActionMap[common_move]`: new `Binding` `kb:i` press -> signal
  `cyclebreaker`, the signal that starts the base game's idle cycle breaker
  (`Main Avatar/Common/IdleCycleBreaker`), which the engine otherwise sends after a spell of idling.
  The guide's weapon-inspecting recipe
  ([weapons](../../../docs/docs/modding/guide/weapons.md#guide---weapon-inspecting)).
- `config/defaultusercontrols.xml`, `CATEGORY_ACTIONS`: new `Control[cyclebreaker]`, `key1` `kb:i`,
  actionmap `common_move_remap`, group 1, conflict mask 12.

## Depends on

- `strings-control-labels`: the control's label "Inspect" (`Actions/cyclebreaker`).
- Scubrah's Patch binds the same signal to I and also keeps the animation from being cut short
  ([`weapon-inspect`](../../scubrahs-patch/features/weapon-inspect.md)); Realism Plus rides it on a
  held reload ([`input-inspect-weapon`](../../realism-plus/features/input-inspect-weapon.md)). This
  mod does not touch the state machine for it.

## Uncertain

- With the base state machine, moving or aiming may end the animation early, as for any idle
  breaker; not checked. No gamepad binding.
