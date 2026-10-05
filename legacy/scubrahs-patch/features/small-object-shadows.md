---
title: Shadows for small terrain objects and vegetation
kind: component
bundle: visuals
claims:
  - "Force enabled shadows for some small terrain objects and vegetation (thanks miru)"
status: located
systems: [graphics]
match:
  - "worlds/*/generated/world*.managers.fcb/collectionmanager.*#SerializationData/ResourcesList/Resource[*]/flags"
  - "engine/settings/defaultrenderconfig.xml#Geometry/quality[*]@*MinSizeShadowScale"
exclude: []
requires: []
verified: diff
---

# Shadows for small terrain objects and vegetation

Scattered rocks, trees and plants that the base game kept from casting shadows now cast them, and
smaller objects pass the size cut-off for shadow casting at every quality level.

## How

**Collection resources.** In both worlds' `world*.managers.fcb`, `CollectionManager`'s
`ResourcesList` holds one `Resource` per scattered model with a `flags` word. The mod clears bit
`0x20` on every resource that had it, 127 of 187 in each world, and on nothing else: 106 -> 74 (55
resources), 110 -> 78 (29), 104 -> 72 (19), 108 -> 76 (12), 105 -> 73 (11), 96 -> 64 (1). The
resources are RealTree trees and bushes (`graphics\vegetation\...\realtrees\*.rtx`), terrain rocks
(`graphics\terrain\rocks\...`) and other vegetation models.

**Size thresholds.** `engine/settings/defaultrenderconfig.xml`, `Geometry`, old -> new:

| Level | `RealTreeMinSizeShadowScale` | `ClusterObjectMinSizeShadowScale` | `SceneObjectMinSizeShadowScale` |
|---|---|---|---|
| `low` | 3.0 -> 2.0 | 3.0 -> 2.0 | 3.0 -> 2.0 |
| `medium` | 2.0 -> 1.25 | 2.0 -> 1.50 | 2.0 -> 1.50 |
| `high` | 1.25 -> 1.0 | 1.50 -> 1.25 | 1.50 -> 1.25 |
| `ultrahigh` | unchanged (1.0) | 1.25 -> 1.0 | 1.25 -> 1.0 |

The Ultra High preset uses `Geometry` `ultrahigh`; High uses `medium`.

## Uncertain

- That bit `0x20` is "casts no shadow" is inferred from the claim and from which resources carry
  it; the reader of the flag is not traced.
- Which of the two halves the claim's credit to miru covers is not stated.
