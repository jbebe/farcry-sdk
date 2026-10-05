---
title: No save-point icons on the map and GPS
kind: component
bundle: navigation
status: located
systems: [ui, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
verified: re
---

# No save-point icons on the map and GPS

Safehouses, bus stops and other save points no longer show their save icon on the map or the GPS.
Unlike the rest of the navigation changes, this is in every variant's `Dunia.dll`, Full Navigation
included.

## How

At start-up, `0x1068B67F` and `0x1068B6AB` (GOG) initialise two global archetype names,
`gadgets.ObjectiveIcons.SaveDisk` (the map icon) and `gadgets.ObjectiveIcons.SaveDiskGPS` (the GPS
icon). By their names, these are the archetypes the engine uses to mark a save point. The mod zeroes
both strings, so the names are empty and no icon archetype can be found by them.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| `…SaveDiskGPS` | `0x10E93370` | `0x10E0AF80` | 34 bytes -> zeros |
| `…SaveDisk` | `0x10E93394` | `0x10E0AFA4` | 31 bytes -> zeros |

## Uncertain

- What the engine does with an empty archetype name (no icon, or a logged failure) was not traced. No
  icon is the expected result.
