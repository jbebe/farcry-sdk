---
title: No depth of field
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#Post/quality[high]@DepthOfField"
exclude: []
requires: []
verified: diff
---

# No depth of field

The depth-of-field blur is switched off. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, `Post/quality[high]`: `DepthOfField` 1 -> 0. Every
preset from High up selects the `high` post-processing level, and the levels below it already had
it off, so no preset draws it now.

## Uncertain

- Which moments use the engine's depth of field (the aiming blur of `legacy/scubrahs-patch`
  `no-ads-blur` is a separate movie-sequence effect) is not traced.
