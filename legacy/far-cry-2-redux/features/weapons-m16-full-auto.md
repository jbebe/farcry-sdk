---
title: M16 fires full auto
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16{,/persistent,/multi}.xml#**/FireStrategyProperties/iBurstLength"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16.xml#**/CommonProperties/sDisplayName"
exclude: []
requires: []
verified: diff
---

# M16 fires full auto

The M16 no longer fires three-round bursts, and it is called by its real name.

## How

- `FireStrategyProperties/iBurstLength` `3` -> `0` on `WeaponProperties.Primary.M16`, `.Persistent`
  and `.Multi` in `generated/entitylibrarypatchoverride.fcb`. The `.Multi` copy is the M16 this mod
  hands to enemies (`weapons-enemy-loadouts`), so theirs fire full auto as well.
- `CommonProperties/sDisplayName` `AR-16` -> `M-16` on `Primary.M16`.

## Depends on

Nothing. Realism Plus sets the same burst length on the player's two copies
([`weapons-m16-full-auto`](../../realism-plus/features/weapons-m16-full-auto.md)). The shop and HUD
names are strings (`strings-weapon-names`).

## Uncertain

- That `iBurstLength` `0` means fully automatic is read from the name; where `sDisplayName` shows
  (if anywhere in single player) is not traced. Not a line of the readme.
