---
title: Decals last about 17 minutes
kind: component
bundle: graphics
status: located
systems: [graphics]
match:
  - "databases/generic/decal.xml#Generic[*]@fLifeTime"
exclude: []
requires: []
verified: diff
---

# Decals last about 17 minutes

Bullet holes, scorch marks, machete scuffs and embers stay on surfaces for 999 seconds instead of
fading after a few seconds to a minute. Blood marks on flesh last 9 seconds instead of 1.

## How

`databases/generic/decal.xml`, `fLifeTime` on all 28 decal types, old -> new in seconds:

- 20 -> 999: `Base.Glass`, `Base.Leather`, `Base.Metal_hard`, `Base.Metal_soft`, `Base.Pebble`,
  `Base.Plastic`, `Base.Small_burn`, `Base.Tile`, `Base.Wood_hard`, `Base.Wood_light`,
  `Grenade_imp.Grenade_ground_hard` and the machete scuffs `B_scuff_ceramic`, `B_scuff_concrete`,
  `B_scuff_metal`, `B_scuff_peeble`, `B_scuff_wood`, `S_scuff_ceramic`, `S_scuff_concrete`,
  `S_scuff_peeble`, `S_scuff_wood`.
- `Base.Concrete` 60 -> 999, `Base.Default` 10 -> 999, `Machete_bounce.B_scuff_carpet`
  0.5 -> 999, `Machete_bounce.B_scuff_plastic` 10 -> 999,
  `Machete_slash.S_scuff_flesh` 2.5 -> 999, `misc.braise` 18 -> 999.
- `Base.gre_ground` 0 -> 999.
- `Base.Flesh` 1 -> 9.

`fFadeOutDuration` is unchanged, and so are the decal caps in `defaultrenderconfig.xml`
(`MaxDecalCount`, `MaxDecalCountPerType`), so on a busy fight the oldest decals are still recycled
when a cap is reached. The settings are described in
`docs/docs/modding/guide/graphics.md#decals---lifetime`, which also warns that a longer
`Base.Flesh` lifetime can leave hit marks floating in the player's view. The mod's 9 s is that
risk, kept short.

`misc.braise` is the glowing-embers decal left by fire, so burnt ground keeps glowing for the
whole 999 s.

## Uncertain

- `Base.gre_ground` was 0. If 0 means "never fades" rather than "gone at once", 999 shortens it.
  The engine's reading of 0 is not traced.
- The ember reading of `misc.braise` comes from its texture, `graphics\GFX\Fires\GFX_braise.XBT`.
