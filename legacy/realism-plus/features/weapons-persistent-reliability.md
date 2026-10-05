---
title: Weapons found in the world start more worn
kind: component
bundle: weapons
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/**/persistent.xml#**/fForcedReliability"
exclude: []
requires: []
verified: diff
---

# Weapons found in the world start more worn

Nine heavy and automatic weapons lying in the open world are in worse condition when picked up.

## How

`CommonProperties/fForcedReliability` `72` -> `50` on the `.Persistent` weapon properties of the
AS50, M16, MGL140, USAS-12, Carl Gustaf, LPO-50, M249, mortar and RPG-7, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`. `.Persistent` is "the version that can be found in the
open world" in the author's guide
([weapons](../../../docs/docs/modding/guide/weapons.md#weapon-entry-titles)).

## Uncertain

- That `fForcedReliability` is the condition (percent) such a weapon starts at is read from the name;
  not traced.
