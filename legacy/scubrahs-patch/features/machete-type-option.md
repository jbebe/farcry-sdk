---
title: Machete type option unlocked
kind: component
bundle: gameplay
claims:
  - "You can now change your machete type in the game options at the main menu by default"
status: unresolved
systems: [weapons, ui]
match: []
exclude: []
requires: []
existing: mods/UFCP — src/fixes/bonus_content.cpp
verified: diff
---

# Machete type option unlocked

Options -> Game offers Machete Type (the Primitive and Homemade variants) without the retired
promotion that used to unlock it.

## How

No data change found; likely one of the Dunia.dll patches (pending trace).

Searched: the four machete weapon-property archetypes (changed, but only for range and creeping -
see `machete-range`), the menu and option strings, and every config and script change. UFCP
restores the same option by patching `IsMachetesUnlocked()` (`FCSE::Uplay(0x000488D0)`); the mod's
`Dunia.dll` has two byte runs just inside that function, `install/bin/dunia.dll@0x488f3` and
`@0x48923`, which are the likely site.
