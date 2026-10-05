---
title: Shop weapon stat bars redrawn
kind: component
bundle: gameplay
status: located
systems: [ui, economy]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/**"
exclude: []
requires: []
verified: diff
---

# Shop weapon stat bars redrawn

`engine/gamemodes/gamemodesconfig.xml`, `WeaponBazaar` → `Summary/Weapons`: 113 `Attribute` values
(`accuracy`, `damage`, `firerate`, `range`, `reliability`) change across the weapons. For example
the Ithaca's damage goes 3 → 1.5 and its reliability 4.5 → 1.0. Several `firerate` values go
0.5 → 1.

These are the bars the arms dealer shows for a weapon. They describe it but do not set its
behaviour, which lives in the weapon's archetype, so this changes what the shop claims, not how the
gun shoots. No price, availability or unlock changes.
