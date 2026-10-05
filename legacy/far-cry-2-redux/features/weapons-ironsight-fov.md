---
title: Iron sights barely zoom
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons]
match:
  - "**/weaponproperties/**#**/IronSight/fIronsightFOV"
exclude:
  - "**/weaponproperties/primary/as50{,/*}.xml#**"
  - "**/weaponproperties/primary/dragunov/*.xml#**"
  - "**/weaponproperties/primary/mgl140{,/*}.xml#**"
  - "**/weaponproperties/primary/m16{,/*}.xml#**"
  - "**/weaponproperties/special/m1903{,/*}.xml#**"
  - "**/weaponproperties/special/dart_rifle{,/*}.xml#**"
  - "**/weaponproperties/dlc1/crossbow{,/*}.xml#**"
  - "**/weaponproperties/special/lpo50/multi.xml#**"
requires: []
existing: mods/UFCP — src/options/fov.cpp (Ironsight field of view option, at run time)
verified: diff
---

# Iron sights barely zoom

Aiming down open sights keeps nearly the hip-fire view: the rifles, machine guns, shotguns and SMGs
no longer zoom in, and the RPG-7 only a little.

## How

`CommonProperties/IronSight/fIronsightFOV` (radians, the view the sights frame,
[first-person aiming](../../../docs/docs/engine-internals/first-person-aiming.md)) on every unscoped
weapon property, the mod's copies in `generated/entitylibrarypatchoverride.fcb` and the DLC shotguns
in `downloadcontent/dlc1/generated/entitylibrary.fcb`:

| From | To | Weapon properties |
|---|---|---|
| `0.95` | `1.3` | `AK47`, `AK47.AK47_Gold`, `FNFAL`, `FNFAL.Persistent`, `G3KA4`, `MP5`, `MP5.Mikes_Rusty`, `MP5.Persistent`, `MountedWeapons.M2_Mounted` |
| `0.925`, `0.93` | `1.3` | `M249_Saw` (with `.Persistent`, `_Merc`), `PKM` (with `.Mikes_Rusty`, `_Merc`) |
| `1` | `1.3` | `Ithaca`, `SPAS12` (with `.Persistent`), `USAS12` (with `.Persistent`), `MAC10` (with `.Mikes_Rusty`), `Uzi`, `DLC1.SilencedShotgun` |
| `1.2155` | `1.3` | `DLC1.SawedOffShotgun` |
| `1.05` | `1.2` | `RPG7`, `.Mikes_Rusty`, `.Persistent`, `.RPG7_Merc` |
| `0.75`, `1.13`, `1.309` | `1.3` | `MountedWeapons.M249_Mounted`, `Special.M2`, `MountedWeapons.MK19_Mounted`, `Special.MK19` |
| `1.308`, `1.30833`, `1.309` | `1.3` | the machetes, `LPO50` (with `.Persistent`), the pistols, `M79`, `IED`, `Flare_Gun`, `Wrong.WrongProperties` |

Rifles go from about 54 to 74.5 degrees, shotguns and SMGs from 57; pistols, machetes and the
flamethrower were already there. The `.Multi` copies get the same `1.3` (from `0.75` to `1.309`),
the RPG-7's `1.2`.

## Depends on

Nothing. The scoped weapons are `weapons-scope-fov`; the slower raise of the sights is
`weapons-ironsight-speed`. Realism Plus widens the same field less far (to `1`-`1.3` by class,
[`weapons-ironsight-fov`](../../realism-plus/features/weapons-ironsight-fov.md)), Scubrah's Patch
only the rifles ([`ads-fov`](../../scubrahs-patch/features/ads-fov.md)). The enemies' flamethrower
`LPO50.Multi` gets `0.6` instead, on `weapons-enemy-flamethrower`.

## Uncertain

- Not a line of the readme; the degrees assume `fIronsightFOV` is the full vertical angle.
