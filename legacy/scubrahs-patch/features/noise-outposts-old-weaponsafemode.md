---
title: Retired SetWeaponSafeMode script
kind: noise
status: located
systems: [player]
match:
  - "_hash/4a9e3fe2.lua"
exclude: []
requires: []
verified: diff
---

# Retired SetWeaponSafeMode script

`_hash/4a9e3fe2.lua` is a replacement `SetWeaponSafeMode` Domino box whose own header says it was
superseded by a DLL patch in update 2.6 and is no longer used.

It does nothing in game: it is not stored at vanilla's `domino/system/setweaponsafemode.lua` (that
file is unchanged, and every script that uses the box registers that path), so nothing loads it.
What it once did is described under `weapon-safe-area-movement`.
