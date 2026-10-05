---
title: Editor-library archetypes copied into the override library
kind: noise
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*/{rusty,dropped}.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weaponscrate/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/binoculars.xml"
exclude: []
requires: []
verified: diff
---

# Editor-library archetypes copied into the override library

62 whole archetypes the mod's `generated/entitylibrarypatchoverride.fcb` adds that the game's
single-player libraries do not have and that nothing uses.

## How

They exist only in the worlds' editor library (`entitylibrary_full.fcb`, which the campaign does not
load), and the library export carried them over:

- the parent pickups `pickups.Weapons.<Weapon>_new` of 19 weapons and `Weapons.SilencedMakarov_6P9`;
  13 `.Rusty` variants; `Weapons.M67.Dropped` and `Weapons.Molotov.Dropped`;
- 26 `pickups.WeaponsCrate.<Weapon>Crate`;
- `gadgets.Equipped.Binoculars`.

The same set (a few bytes larger here) is in Realism Plus's library, analysed there
([`noise-weapons-editor-library-copies`](../../realism-plus/features/noise-weapons-editor-library-copies.md)):
no single-player archetype names any of them.

## Uncertain

- A sector entity naming one of these as its template was not searched for.
