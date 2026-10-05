---
title: Fire or aim to draw the holstered weapon
kind: component
bundle: controls
claims:
  - "Attempting to fire while holstered will draw the weapon (Thanks for the tip, Tom)"
status: located
systems: [weapons, input]
match:
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[9]/Event[+1]"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[9]/Event[+2]"
exclude: []
requires: []
verified: diff
---

# Fire or aim to draw the holstered weapon

With the weapon holstered, pressing fire or aim draws it instead of doing nothing.

## How

`scripts/engine/objects/pawn/statemachine/weapons.gosm.xml`, group
`Pawn Weapons/Weapon Mechanics/States/AllowWeaponSwitch` (`Group[9]`), gains two events beside the
base game's `Try draw` on `drawweapon`, both copies of it: `Try draw` (`CGOStateEventInventory`,
`requestType` `5`) on the signals `startshooting` and `startironsight`.

## Depends on

- Matters only while the weapon can stay holstered: `weapons-auto-holster` and `input-holster-key`.
- Realism Plus returns to idle on `startshooting` from its own holstered state instead
  ([`weapons-holster`](../../realism-plus/features/weapons-holster.md)).

## Uncertain

- Whether a fire press while armed also fires a request to draw (harmless, the weapon being out) is
  not checked.
