---
title: Widescreen field of view setting
kind: component
bundle: graphics
claims: []
status: located
systems: [graphics]
match:
  - "engine/settings/defaultrenderconfig.xml#@WidescreenFOV"
exclude: []
requires: []
verified: diff
---

# Widescreen field of view setting

A render setting the base file does not write is set: `WidescreenFOV`. Not a line of the readme.

## How

`engine/settings/defaultrenderconfig.xml`, root `Profile`: new attribute `WidescreenFOV` "1". The
name is read by Steam's `Dunia.dll` (it is among its strings); the base file leaves it at the
engine default.

## Uncertain

- What the engine does with it (a wider horizontal view on widescreen displays, or the opposite) and
  what its default is are not traced.
