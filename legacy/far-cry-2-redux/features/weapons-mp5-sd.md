---
title: The MP5SD is a silent SMG
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/mp5{,/mikes_rusty,/persistent}.xml#**"
exclude:
  - "**#**/{bAutoReload,fIronsightFOV,fIronsightTransitionTime}"
requires: []
verified: diff
---

# The MP5SD is a silent SMG

The integrally silenced MP5 is treated as one: enemies do not hear it, it uses SMG ammunition, throws
pistol-calibre brass, has no muzzle flash on any copy, and its shots alert a smaller area.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb` of `WeaponProperties.Primary.MP5`,
`.Mikes_Rusty` and `.Persistent`:

- `CommonProperties/bIsSilent` `False` -> `True` (all three)
- `Ammo/ammoAmmoType` `BC6782FC` (`assaultrifle`) -> `AA73EE0A` (`smg`), with the `text_` twin (all
  three)
- `Particles/psBulletCaseParticleId` `weapons.weapons.brass_ak47` -> `weapons.weapons.brass_desert_eagle`
  (all three)
- `Particles/psMuzzleParticlesId` and `psMuzzleParticlesId_3rd` (`pl_muzzleflash_sniper_riffle`,
  `muzzleflash_sniper_riffle`) -> none on `.Mikes_Rusty` and `.Persistent`; the armory MP5 already
  had none
- `fRange` `300` -> `280` (`MP5`, `.Mikes_Rusty`)
- `MuzzleStims/Stims/Stim[0]/fRadius` `5` -> `3` and `ImpactStims/Stims/Stim[0]/fRadius` `4` -> `3`
  (`MP5`)

## Depends on

Nothing. Realism Plus makes the same ammunition switch
([`weapons-mp5-smg`](../../realism-plus/features/weapons-mp5-smg.md)), and also moves the MP5's
ammo upgrade to the light assault webbing; this mod leaves its `maxammo` bonuses in the assault
webbing (`economy-ammo-upgrades`), so with SMG ammo the upgrade that raises the MP5's reserve may
no longer be the one that names it.

## Uncertain

- `bIsSilent` as "not heard by the AI" and the stim radii as the alert area are read from the
  names; not traced. Not a line of the readme.
