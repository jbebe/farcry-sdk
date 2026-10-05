---
title: Holster the weapon
kind: component
bundle: gameplay
status: located
systems: [weapons, input, player]
match:
  - "scripts/engine/objects/pawn/statemachine/main_avatar.gosm.xml#**"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[*]/StateRef[+*]"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#State[+1]"
  - "graphics/characters/_common/animations/weapons/**/*1stge_uppb_holster_+000fw_*_i1.mab"
exclude: []
requires: [input-holster-key]
verified: diff
---

# Holster the weapon

Pressing X (holding Y on a pad) puts the weapon away into a new holstered state; drawing it again,
shooting or using something brings the player back to the normal idle state.

## How

This is "Option 2 - New holstered state" of the author's own guide
([weapons](../../../docs/docs/modding/guide/weapons.md#option-2---new-holstered-state)).

- **The state.** `scripts/engine/objects/pawn/statemachine/main_avatar.gosm.xml`: `Main Avatar/Common/Idle`
  (`State[2]`) gains a sink `HolsterWeapons` on signal `HolsterWeapons` that leads to a new
  `Pawn Weapons/Weapon Mechanics/States/HolsterWeapons` state in `weapons.gosm.xml` (`State[+1]`:
  layer `Pawn_Generic_Holster`, a `Holster` inventory event, `requestType` `4`, at its end). That
  state hands over to a new `Main Avatar/Common/WeaponsHolsteredState` (`State[+3]`): the idle
  movement with the `Pawn_Generic_Wait` layer, keeping the idle state's heal, syringe, malaria,
  use, mounted-weapon, vehicle, grenade, slide, pickup, briefcase and ladder hooks, and returning to
  `Common/Idle` on `startshooting` or `use`.
- **Groups.** The new state is added to four `weapons.gosm.xml` groups so the player can still use
  gadgets, sprint, switch weapons and open the map while holstered: `Allow Gadget Use` (`Group[1]`),
  `CanSprintGroup` (`Group[2]`), `AllowWeaponSwitch` (`Group[9]`) and `MapCompass/AllowMapCompass`
  (`Group[34]`).
- **Animations.** Twelve first-person holster animations are replaced, so arms do not stay raised
  after holstering: the MGL140's `1stge_uppb_holster_+000fw_prmgl_i1.mab` is edited (25 bytes), and
  copies of it, each with its own weapon's sound bytes (2-5 bytes differ), replace those of the AK-47
  (`prak4`), G3KA4 (`prg3k`), Ithaca (`prith`), MP5 (`semp5`), M249 (`spsaw`), Dart Rifle (`sp389`),
  mortar (`spmrt`), silenced shotgun (`prsso`), sawed-off (`sesos`) and crossbow (`spcrb`); the
  SPAS-12's (`prspa`) is edited in place (26 bytes). All are under
  `graphics/characters/_common/animations/weapons/` in the mod's `patch.dat`.
## Depends on

- `input-holster-key`: the X / pad Y binding that sends the `HolsterWeapons` signal, the rebindable
  `Control[holster]` and its label. Without it nothing sends the signal.
- Scubrah's Patch has a holster key of its own built differently, on the existing holster request
  without a new state or animations
  ([`weapon-holster`](../../scubrahs-patch/features/weapon-holster.md)).

## Uncertain

- The guide notes that a holstered player must unholster before entering buildings (press use twice);
  not checked here.
- `Binding[+8]` and `Binding[+24]` in the same action map (`kb:r` / `pad:x` hold on `cyclebreaker`)
  are weapon inspection, `input-inspect-weapon`.
