---
title: Holster key (N)
kind: component
bundle: controls
claims:
  - "Holster - N"
  - "Added a manual holster function"
  - "Added key rebinds for holster and inspect in the Options menu"
status: located
systems: [input, weapons]
match:
  - "config/defaultusercontrols.xml/category_actions.xml#Control[holsterweapon]"
  - "config/inputactionmapcommon.xml/common_move.xml#Binding[+6]"
exclude: []
requires: [weapons-auto-holster, strings-control-labels]
verified: diff
---

# Holster key (N)

Pressing N puts the weapon away; the key can be rebound in the Options menu as "Holster".

## How

- `config/inputactionmapcommon.xml`, `ActionMap[common_move]`: new `Binding` `kb:n` press -> signal
  `holsterweapon`.
- `config/defaultusercontrols.xml`, `CATEGORY_ACTIONS`: new `Control[holsterweapon]`, `key1` `kb:n`,
  actionmap `common_move_remap` (which `common_move` already imports), group 1, conflict mask 12.

The signal already exists in the base game: `weapons.gosm.xml`'s `AllowWeaponSwitch` group carries
a `Try holster` event on `holsterweapon` (and `Try draw` on `drawweapon`), with no key bound to it.
No state machine change is needed.

## Depends on

- `weapons-auto-holster`: with auto draw on, the engine draws the weapon right back (the guide's
  reason for disabling it).
- `strings-control-labels`: the control's label "Holster" (`Actions/holsterweapon`), without which
  the Options menu shows the control unnamed.
- `input-map-key-tab` moves the gadget key off 5 "to prevent conflict with holster".
- Realism Plus (X) and Scubrah's Patch (Y) bind holster keys of their own built differently
  ([`input-holster-key`](../../realism-plus/features/input-holster-key.md),
  [`weapon-holster`](../../scubrahs-patch/features/weapon-holster.md)).

## Uncertain

- No gamepad binding is added. Drawing again uses the weapon keys or `weapons-fire-to-draw`.
