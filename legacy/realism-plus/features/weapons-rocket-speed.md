---
title: RPG and Carl Gustaf rockets fly twice as fast
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weapons/rockets/{rpg7rocket,carlgustafrocket}.xml#**"
exclude: []
requires: []
verified: diff
---

# RPG and Carl Gustaf rockets fly twice as fast

Rockets from the RPG-7 and the Carl Gustaf reach their target sooner.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`:

- `weapons.Rockets.RPG7Rocket`: `CRocket/Stages/Fire/fSpeed` `25` -> `50`
- `weapons.Rockets.CarlGustafRocket`: `CRocket/Stages/Ignite/fImpulse` `75` -> `150`

These are the two speed fields the guide names for these projectiles
([weapons](../../../docs/docs/modding/guide/weapons.md#speed)).

## Uncertain

- Both values double; the paraphrased v2.1 list says rockets are about 40% faster. The resulting
  flight speed (impulse against mass for the Carl Gustaf) is not traced.
