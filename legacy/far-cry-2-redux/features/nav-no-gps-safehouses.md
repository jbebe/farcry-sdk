---
title: No safehouses on the GPS
kind: component
bundle: navigation
claims: []
status: located
systems: [ui]
match:
  - "graphics/objects/mapcompass/icon_safehouse{,_2}_{gps,gpsveh}.xbg"
exclude: []
requires: []
verified: diff
---

# No safehouses on the GPS

The GPS, on foot and in a vehicle, no longer marks safehouses, locked or unlocked. The paper map
still does (with the green house of `ui-map-icons`, once unlocked).

## How

`graphics/objects/mapcompass/icon_safehouse_gps.xbg`, `icon_safehouse_gpsveh.xbg`,
`icon_safehouse_2_gps.xbg` and `icon_safehouse_2_gpsveh.xbg`, the four GPS safehouse marker models
(`_2` is the locked one), are all replaced by one 912-byte file: the base `icon_savegpsveh.xbg`
with every zero byte (348 of them) turned into a space (`0x20`). Its chunk sizes and counts become
garbage, so it no longer loads as a model (JackAll's reader stops at offset 0x40) and the markers
draw nothing. The archetypes that place the markers
(`ObjectiveIcons.SafeHouse_{Locked,Unlocked}_GPS{,_VEH}`) are untouched.

The same broken file also replaces `icon_savegps.xbg` (`nav-no-save-icons`).

`legacy/realism-plus` `nav-gps-icons-removed` gets the same result by removing the model from the
archetypes instead.

## Uncertain

- The zero-to-space substitution looks like a file passed through a text tool; whether it was meant
  as a blanking trick is not stated. Its effect (no marker) is inferred from the file failing to
  parse; how the engine handles the failure is not traced.
