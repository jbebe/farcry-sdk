---
title: Static objects' descriptors wrapped in extra rml elements by the mod's converter
kind: noise
status: located
systems: [world]
match:
  - "levels/w1_c_3/generated/worldsectors/worldsector{2756,3315}.data.fcb/*#Components/CFileDescriptorComponent/hidDescriptor/{hidDescriptor,rml}"
exclude: []
requires: []
verified: diff
---

# Static objects' descriptors wrapped in extra rml elements by the mod's converter

Not an edit, 110 changes: a by-product of the two Pala sectors the mod rewrote for
`world-pala-guard-posts-removed`.

In `levels/w1_c_3` sectors 2756 and 3315, every `StaticObject_*` and `VisualObject_*` the mod
keeps - 47 in sector 2756, 8 in 3315, all of the sectors' surviving entities that carry one -
has its embedded `CFileDescriptorComponent/hidDescriptor` (the rock or building model, its bounding
boxes and physics shape) written back inside two extra `<rml>` elements. The content inside is
unchanged. Each reads as the `hidDescriptor` removed and an `rml` added.

This is the round-trip bug of Gibbed's converter that wobatt's modified tools fix ("XML->FCB->XML
round-trips injected spurious nested `<rml>` elements",
[getting started](../../../docs/docs/modding/getting-started.md)). The same wrapping shows on the
mod's override-library pickups and weapons, nine levels deep on some of them (other pages).

## Uncertain

- Whether the engine reads a placed object's own `hidDescriptor` at run time, and so whether the
  extra wrapping could stop one of these objects drawing, is not traced; that the mod plays with
  the town's rocks and buildings in place suggests it does not matter.
