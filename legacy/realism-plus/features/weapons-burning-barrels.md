---
title: Exploding barrels start fires
kind: component
status: located
systems: [weapons, world]
match:
  - "generated/entitylibrarypatchoverride.fcb/oa_explosives/explosives/explosivebarrel_new.xml#**"
exclude: []
requires: []
verified: diff
---

# Exploding barrels start fires

An exploding fuel barrel also burns what stands close to it.

## How

`OA_Explosives.Explosives.ExplosiveBarrel_NEW`, the mod's copy in
`generated/entitylibrarypatchoverride.fcb`, gains a third stim in
`CCompoundPhysComponent/RootNode/States/State[0]/StateSubNode/Explosion/ExtraStims`: `selType` `7`
(`Burn`), `nLevel` `15`, `fRadius` `3`, `bFalloff` `True`, `nFalloffMinLevel` `1`.

## Uncertain

- Whether the burn stim sets grass alight or only hurts pawns in range is not checked.
