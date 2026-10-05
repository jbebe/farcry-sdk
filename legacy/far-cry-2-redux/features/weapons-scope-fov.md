---
title: Scope zoom of the scoped rifles
kind: component
bundle: weapons
claims:
  - "Lowered Ironsight FOV of the scoped rifles. They should no longer \"float\"."
status: located
systems: [weapons]
match:
  - "**/weaponproperties/primary/as50{,/*}.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/primary/dragunov/*.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/primary/mgl140{,/*}.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/primary/m16{,/*}.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/special/m1903{,/*}.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/special/dart_rifle{,/*}.xml#**/IronSight/fIronsightFOV"
  - "**/weaponproperties/dlc1/crossbow{,/*}.xml#**/IronSight/fIronsightFOV"
exclude: []
requires: []
verified: diff
---

# Scope zoom of the scoped rifles

The magnified sights get a new field of view each, set so that the scope picture sits still rather
than "floating" against the world (the readme's 6-6-20 line).

## How

`CommonProperties/IronSight/fIronsightFOV` on the weapons with a magnifying sight, the mod's copies
in `generated/entitylibrarypatchoverride.fcb` and `downloadcontent/dlc1/generated/entitylibrary.fcb`:

| Weapon properties | From | To |
|---|---|---|
| `Primary.AS50`, `.Persistent` | `0.2` | `0.28` |
| `Primary.Dragunov.AI`, `.Dragunov_Merc`, `.Persistent` | `0.28` | `0.3` |
| `Special.M1903` | `0.285` | `0.28` |
| `Special.M1903.M1903_Merc` | `0.285` | `0.3` |
| `Primary.MGL140`, `.Persistent` | `0.29` | `0.3` |
| `Primary.M16`, `.Persistent` | `0.6` | `0.76` |
| `Special.Dart_Rifle` | `0.3` | `0.25` |
| `DLC1.Crossbow` | `0.28` | `0.5` |

The `.Multi` copies of the AS50, Dragunov, M1903, MGL140 (`0.3`), M16 (`0.8`) and crossbow (`0.5`)
change alike. The player's own `Primary.Dragunov` is not in the mod's library and keeps `0.28`.

## Depends on

Nothing. The scope reticle textures are `weapons-scope-reticles`; the open sights are
`weapons-ironsight-fov`.

## Uncertain

- "Lowered" does not match most values, which rise (less zoom); only the M1903's and the dart
  rifle's fall. What "floating" was (the scope overlay drifting against the world while aiming) is
  the readme's word, not checked in game.
- `Dragunov.AI` and the `_Merc` copies are carried by enemies, whose aim does not use the player's
  scope view, so their values likely change nothing.
