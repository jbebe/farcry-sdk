---
title: Flare gun on the gadget slot
kind: component
bundle: controls
claims:
  - "Flare gun - Tap 1 twice"
status: located
systems: [weapons, input]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/flare_gun.xml#**/selCategory"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/flare_gun.xml#**/AIShootingSystem/*"
exclude: []
requires: []
verified: diff
---

# Flare gun on the gadget slot

The flare gun no longer takes the secondary slot: it sits on the extra slot reached by pressing the
machete key (1) twice, so a pistol or SMG can be carried beside it.

## How

`WeaponProperties.Special.Flare_Gun`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`:

- `CommonProperties/selCategory` (`2`, Secondary) is removed. A weapon without `selCategory` lands on
  the extra slot, per the author's guide
  ([weapons](../../../docs/docs/modding/guide/weapons.md#extragadget-slot)), which allows one weapon
  there.
- `AIShootingSystem/archTargetDistanceCurve` and `archSuccessfulHitCurve` are filled in (empty ->
  `Curves.ShootingSystem.DistanceAccuracy`, `Curves.ShootingSystem.SuccessfulHit`), which only an AI
  holding this archetype would read; enemies carry `Flare_Gun_Merc`.

## Depends on

- `strings-machete-flare-gun` renames the machete control and tutorial text ("Machete and Flare
  Gun"); not needed for the slot to work.
- The bazaar sells the flare gun from the start without a convoy unlock (`economy-shop-unlocked`).
- Realism Plus makes the identical `selCategory` change
  ([`weapons-flare-gun-gadget`](../../realism-plus/features/weapons-flare-gun-gadget.md)).

## Uncertain

- How the engine places an archetype without `selCategory` is the guide's account, not traced.
