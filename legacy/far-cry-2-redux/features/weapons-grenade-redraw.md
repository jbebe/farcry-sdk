---
title: Weapon drawn again after a grenade throw
kind: component
bundle: controls
claims:
  - "Throwing a grenade no longer holsters your weapon"
status: located
systems: [weapons]
match:
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#State[27]/Connection@Target"
exclude: []
requires: []
verified: diff
---

# Weapon drawn again after a grenade throw

After throwing a grenade or Molotov the player's weapon comes back up instead of staying away.

## How

`scripts/engine/objects/pawn/statemachine/weapons.gosm.xml`, state
`Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade` (`State[27]`): its exit
`Connection@Target` `::Pawn Weapons/External States/Main Avatar/Common/xIdle` ->
`::Pawn Weapons/Weapon Mechanics/States/Drawing`, so the throw flows into the draw animation, then
idle. Step 2 of the guide's "Disabling Auto Draw", with the grenade as its worked example
([weapons](../../../docs/docs/modding/guide/weapons.md#option-1---disabling-auto-draw)).

## Depends on

- `weapons-auto-holster`: with auto draw off, the throw left the weapon holstered; this restores
  the draw for that one action. Picked without it, the throw plays the draw where the base game
  already drew automatically (inference).
