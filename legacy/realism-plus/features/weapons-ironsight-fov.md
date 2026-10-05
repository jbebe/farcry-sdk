---
title: Iron-sight zoom evened out
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/**#**/fIronsightFOV"
  - "downloadcontent/dlc1/generated/entitylibrary.fcb/weaponproperties/**#**/fIronsightFOV"
exclude:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/dlc1/**"
  - "**/multi.xml#**"
requires: []
existing: mods/UFCP — src/options/fov.cpp (Ironsight field of view option, at run time)
verified: diff
---

# Iron-sight zoom evened out

Aiming down the sights zooms in less on most guns, so the sight picture shows more around the target.

## How

`CommonProperties/IronSight/fIronsightFOV` (radians, the field of view the sights frame the view at,
[first-person aiming](../../../docs/docs/engine-internals/first-person-aiming.md)) on 36 single-player
weapon properties, the mod's copies in `generated/entitylibrarypatchoverride.fcb` and the two DLC
shotguns in `downloadcontent/dlc1/generated/entitylibrary.fcb`:

| From | To | Weapon properties |
|---|---|---|
| `0.95` | `1` | `AK47`, `AK47.AK47_Gold`, `FNFAL`, `FNFAL.Persistent`, `G3KA4` |
| `0.925`, `0.93` | `1` | `M249_Saw`, `M249_Saw.Persistent`; `PKM`, `PKM.Mikes_Rusty` |
| `0.95` | `1.15` | `MP5`, `MP5.Mikes_Rusty`, `MP5.Persistent` |
| `1` | `1.1` | `Ithaca`, `SPAS12`, `SPAS12.Persistent`, `USAS12`, `USAS12.Persistent`, `DLC1.SilencedShotgun` |
| `1` | `1.3` | `MAC10`, `MAC10.Mikes_Rusty`, `Uzi` |
| `1.309` | `1.3` | `Makarov`, `SilencedMakarov_6P9`, `Star45`, `DesertEagle`, `DesertEagle.Persistent`, `M79`, `M79.Mikes_Rusty`, `Flare_Gun` |
| `1.2155` | `1.3` | `DLC1.SawedOffShotgun` |
| `1.05` | `1.15` | `RPG7`, `RPG7.Mikes_Rusty`, `RPG7.Persistent` |
| `0.75`, `0.95`, `1.309` | `1.15` | `MountedWeapons.M249_Mounted`, `M2_Mounted`, `MK19_Mounted` |

Rifles go from about 54 to 57 degrees, the MP5 and the mounted guns to 66, the SMGs and pistols to
74.5. Only the mounted MK19 zooms in more than before (75 -> 66 degrees).

## Depends on

Nothing. Scubrah's Patch widens the rifles' sights the same way, `0.95` -> `1`
([`ads-fov`](../../scubrahs-patch/features/ads-fov.md)). The machetes' `1.308` -> `1.3` belongs to
`weapons-machete-creeping`; the dead edits of this field in the override library's DLC copies are
`noise-weapons-dead-dlc-copies`.
