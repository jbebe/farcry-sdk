---
title: Machine gunners, rocket men, mortar men and snipers look African
kind: component
bundle: gameplay
status: located
systems: [ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{blue,red}_faction/*_caucasian.xml#Entity/Components/CGraphicKitComponent/**"
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/blue_faction/sniper_caucasian.xml#**/fDecelerationsFast"
exclude: []
requires: []
verified: diff
---

# Machine gunners, rocket men, mortar men and snipers look African

The white variants of four soldier classes are replaced by their black counterparts' looks, so these
classes always appear as black Africans.

## How

In the soldier copies the mod puts in `generated/entitylibrarypatchoverride.fcb`, six `_Caucasian`
archetypes take the `CGraphicKitComponent` of their `_Nubian` twin: the specialisation tag
`caucasian` -> `nubian` and the `PartOverwrite` list (head, clothing and gear parts with their
texture and colour indices) rewritten part by part:

- `Blue_Faction.LightMachineGunner_Caucasian`, `MortarMan_Caucasian`, `RocketMan_Caucasian`,
  `Sniper_Caucasian`
- `Red_Faction.LightMachineGunner_Caucasian`, `RocketMan_Caucasian`

Diffed against the `_Nubian` archetype of the same class in the world library, each copy differs only
in its name, id, `fMaxSlope` rounding and the sight multiplier of `ai-stealth-precombat`. That
includes `Blue_Faction.Sniper_Caucasian`'s `CGameAgent/fDecelerationsFast` `-3.5` -> `-6`, the
Nubian sniper's value, carried over by the copy.

This is the "Ethnicity" recipe of Boggalog's guide
([enemies](../../../docs/docs/modding/guide/enemies.md#ethnicity)). It goes with the mod's other
ethnicity swaps: patrol crews (`patrols-blue-crews`, `patrols-red-faction-crews`) and reinforcements
(`ai-reinforcements-nubian`).

## Uncertain

- Riflemen and shotgunners keep both variants; why only these four classes change is not stated.
- Placed soldiers whose sector instance carries its own `CGraphicKitComponent` keep their look.
