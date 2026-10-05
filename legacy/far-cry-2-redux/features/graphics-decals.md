---
title: Decals and wounds that stay
kind: component
bundle: graphics
claims:
  - "Wounds/decals stay on screen longer"
status: located
systems: [graphics]
match:
  - "databases/generic/decal.xml#**"
  - "engine/settings/defaultrenderconfig.xml#Geometry/quality[*]@MaxDecalCount*"
exclude: []
requires: []
verified: diff
---

# Decals and wounds that stay

Bullet holes, scuffs, scorch marks and machete marks no longer fade after 10-60 seconds but stay
until the decal budget recycles them, the budget is several times larger on Ultra High, and bullet
wounds on bodies last 30 seconds instead of one. Several decals are also made larger, two-sided and
given depth.

## How

`databases/generic/decal.xml`, the 28 decal types (`Generic[i]`, base-game index), 136 changes:

- **Lifetime** (`fLifeTime`, seconds) -> 9999 on 26 of 28: the bullet impacts `Base.Concrete` (60),
  `Base.Default` (10), `Glass`, `Leather`, `Metal_hard`, `Metal_soft`, `Pebble`, `Plastic`,
  `Small_burn`, `Tile`, `Wood_hard`, `Wood_light` (20), the grenade mark, the machete's bounce and
  slash scuffs (0.5-20) and `misc.braise` (18). `Base.gre_ground` keeps its own.
- **Wounds.** `Base.Flesh`, the bullet hole on a body: `fLifeTime` 1 -> 30, `fFadeOutDuration`
  0.3 -> 1, parallax on with height -0.01.
- **Bigger default impact.** `Base.Default` (the fallback for surfaces without their own) and the
  machete's `B_scuff_carpet` are given one shared look: size 0.22 (carpet 0.01 x 0.3) -> 0.95 with
  0.45 variance, rotation variance 270, fade-in 0.05 s, fade-out 2.5 s, parallax height 0.04;
  `Default` also `selBlendingType` 0 -> 1, carpet `fAlpha` 0.5 -> 1.
- **Plastic and ceramic.** `Base.Plastic` and `B_scuff_ceramic` likewise share one look: three
  texture variations instead of one, uniform size 0.06 / 0.08 (was 0.1 / 0.01 x 0.2 with variance),
  rotation 60 with variance 220, fade-out 0.2 s, alpha variance 0.1, parallax height 0.13.
- **Everywhere.** `fAnimFrequence` 1 -> 2 on 27 (`misc.braise` 0.4 -> 0.6), `bTwoSided` 0 -> 1 on
  26 (not `Glass` or `braise`), `bParallaxEnabled` 0 -> 1 on 9.

`engine/settings/defaultrenderconfig.xml`, `Geometry/quality[ultrahigh]`: `MaxDecalCount` 200 ->
1500, `MaxDecalCountPerType` 50 -> 1000. The Ultra High presets use this geometry level; the others
keep 200 / 50, so with a long lifetime their decals are recycled sooner.

## Compared with other mods

`legacy/scubrahs-patch` and `legacy/realism-plus` `graphics-decals` raise the same counts (to 2000
/ 1000 and 1600-2000 / 800-1000) but extend only the glass decal's lifetime.

## Uncertain

- "Wounds" in the readme is read as the `Base.Flesh` decal. Blood on characters' own textures
  (`BloodTexture` in the cloth materials) is a separate system, not changed here.
- What `fAnimFrequence` and `selBlendingType` 1 do is not traced.
