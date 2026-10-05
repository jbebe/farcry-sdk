---
title: Disabled particle emitters switched on
kind: component
bundle: improved-graphics
claims: []
status: located
systems: [graphics]
match:
  - "worlds/*/generated/world*_deploadnewparticles.rml#**@Active"
  - "engine/settings/defaultrenderconfig.xml#Environment/**"
exclude: []
requires: []
verified: diff
---

# Disabled particle emitters switched on

Fifteen emitters the base game ships switched off now play: heat haze, smoke and extra fire layers on
several effects. Rain also throws twice as many splashes on Ultra High. Not a line of the feature
list; part of Improved Graphics' "and more" by the analysis's reading.

## How

`world1_deploadnewparticles.rml` and `world2_deploadnewparticles.rml`, `PartEmit@Active` 0 -> 1 on
the same 15 emitters in each world:

| Particle system | Emitters switched on |
|---|---|
| `healing_effects.heal.matches_heal` | `haze`, `haze2` |
| `fire_propagation.fire_propagation.fire_object_e` | `Fire_object_1` |
| `fire_propagation.fire_propagation.smok_ground_b` | `smok2` |
| `grenade_impact.grenade_impact.gre_imp_water` | `dust_ascen2` |
| `weapons.weapons.molotov` | `Fire_object_1`, `Fire_object_2` |
| `weapons.weapons.flare_gun_loop` | `haze` |
| `weapons.weapons.pl_muzzleflash_desert_eagle` | `heat` |
| `weapons.weapons.pl_muzzleflash_as50`, `muzzleflash_as50` | `smok` |
| `weapons.weapons.pl_muzzleflash_ithaca` | `smok2` |
| `weapons.weapons.muzzleflash_m249saw` | `smok` |
| `weapons.weapons.pl_muzzleflash_pkm`, `muzzleflash_pkm` | `smok` |

`engine/settings/defaultrenderconfig.xml`, `Environment/quality[ultrahigh]`:
`RainNumSplashesPerSecond` 35 -> 70.

## Uncertain

- The fire and molotov emitters may have been switched on for `fire-spread` rather than for looks;
  the mod does not say.
