---
title: Unused critical-heal binding
kind: noise
claims: []
match:
  - "config/inputactionmapcommon.xml/common_gameplay.xml#Binding[{+4,+16}]"
  - "config/inputactionmapcommon.xml/common_briefinginteraction.xml#Binding[+3]"
exclude: []
requires: []
verified: diff
---

# Unused critical-heal binding

`inputactionmapcommon.xml` `ActionMap[common_gameplay]` gains `kb:h` hold -> `criticalheal` and
`pad:left_shoulder` hold -> `criticalheal`, beside the base game's press -> `heal` on the same keys.

Nothing consumes `criticalheal`: no state machine in the base game or the mod has an event or sink
on it, and its CRC32 (`6016242F`) is in neither the Steam `Dunia.dll` nor the mod's, whereas the
engine's own signals bound the same way (`jump`, `lock_sprint`, `cyclebreaker`) are. A held `H`
therefore sends a signal no one listens to.

The same binding added to `common_briefinginteraction`, with the jump and sprint bindings it comes
with, is left to the page that owns that action map.
