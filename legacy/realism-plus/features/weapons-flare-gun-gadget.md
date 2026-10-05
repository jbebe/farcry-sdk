---
title: Flare gun on the gadget slot
kind: component
bundle: weapons
status: located
systems: [weapons, input]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/flare_gun.xml#**/selCategory"
exclude: []
requires: []
verified: diff
---

# Flare gun on the gadget slot

The flare gun no longer takes the secondary slot: it sits on the extra "gadget" slot, reached by
pressing the machete key twice, so a pistol or SMG can be carried beside it.

## How

`WeaponProperties.Special.Flare_Gun`, the mod's copy in `generated/entitylibrarypatchoverride.fcb`,
has no `CommonProperties/selCategory` (the base game's is `2`, Secondary). A weapon without
`selCategory` lands on the extra slot, per the author's guide
([weapons](../../../docs/docs/modding/guide/weapons.md#extragadget-slot)), which also says only one
weapon may use that slot.

## The IED choice

The mod ships each patch variant twice, with a flare-gun or an IED gadget. The analysis is of the
flare-gun variant. In the IED variant the only difference in the whole patch archive is inside
`generated/entitylibrarypatchoverride.fcb` (both copies 12,007,564 bytes; decoded, exactly two lines
differ):

- `WeaponProperties.Secondary.IED` loses `selCategory` `2`, so the IED takes the gadget slot;
- `WeaponProperties.Special.Flare_Gun` keeps `selCategory` `2`, as in the base game.

That variant's changes are not part of this analysis and cannot be picked from it. Scubrah's Patch
does the IED version ([`ied-own-slot`](../../scubrahs-patch/features/ied-own-slot.md)), there with a
key of its own.

## Depends on

- The flare gun is also sold from the start without an unlock and listed among the secondaries in
  the bazaar; that is part of `economy-bazaar-reorder`.

## Uncertain

- How the engine places an archetype without `selCategory` is the guide's account, not traced.
