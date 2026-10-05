---
title: Nested descriptors wrapped in extra rml layers
kind: noise
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/{pickups,weapons,pile_archetypes}/**#**/{hidDescriptor,WeaponStatusSwitchValues}/{hidDescriptor,WeaponStatusSwitchValues,rml}"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/**#**/WeaponStatusSwitchValues/{WeaponStatusSwitchValues,rml}"
exclude: []
requires: []
verified: diff
---

# Nested descriptors wrapped in extra rml layers

504 changes, 252 pairs of a removed and an added nested value, that are the same content re-encoded:
the mod's export tool wrapped each nested RML value in one or more extra `<rml>` elements.

## How

Two nested RML fields are affected: `CFileDescriptorComponent/hidDescriptor` (the graphic and
physics descriptor) on 145 pickup, 64 weapon and 3 pile archetypes in
`generated/entitylibrarypatchoverride.fcb`, and `CFCXWeapon/WeaponStatusSwitchValues` on 34 weapons
there and the six DLC weapons in `downloadcontent/dlc1/generated/entitylibrary.fcb`. Each shows as a
`remove` of the base `<hidDescriptor>` (or `<WeaponStatusSwitchValues>`) and an `add` of an `<rml>`
holding it. With the `<rml>` tags stripped, all 252 pairs are identical to the base content. The
wrapping is one layer on 185 values and nine on 67 (the `hidDescriptor` of the 64 multiplayer
weapons, explosives and grenades and the 3 multiplayer pickup piles), as if each pass of the
author's tool added one. All the weapon and pile archetypes involved are `.Multi` ones; the pickups
are single-player and multiplayer alike.

## Uncertain

- The mod plays with these values, so the engine evidently reads past the extra layers (inference);
  a pick keeps the base encoding. `levels/w1_c_3` sector entities carry the same wrapping, outside
  this page's containers.
