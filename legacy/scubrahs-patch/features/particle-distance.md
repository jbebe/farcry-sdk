---
title: Particle effect distance
kind: component
bundle: visuals
claims:
  - "Increased the maximum visible distance of all particle effects"
status: located
systems: [graphics]
match:
  - "worlds/*/generated/world*_deploadnewparticles.rml#**@{MaxEmitDist,FadeEmitDist}"
exclude: []
requires: []
verified: diff
---

# Particle effect distance

Every particle emitter in the campaign keeps emitting three times further from the camera, and
starts fading three times further out.

## How

`worlds/world1/generated/world1_deploadnewparticles.rml` and the `world2` copy, the campaign's
particle library (`NewPartLib`). On every `PartEmit` and `PartChildEmit`, `MaxEmitDist` and
`FadeEmitDist` are multiplied by 3 (e.g. 100 -> 300 and 75 -> 225, 500 -> 1500 and 400 -> 1200):
2,054 values in `world1`, 2,194 in `world2`.

The same files also switch 15 disabled emitters on; that is `graphics-more-particles`.
