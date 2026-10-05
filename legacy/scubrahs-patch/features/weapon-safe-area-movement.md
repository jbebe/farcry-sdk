---
title: Freer movement in weapon safe areas
kind: component
bundle: gameplay
claims:
  - "Allowed for greater freedom of movement within weapon safe areas"
status: unresolved
systems: [player]
match: []
exclude: []
requires: []
verified: diff
---

# Freer movement in weapon safe areas

Weapon safe areas, where the game holsters the player's weapon (the faction headquarters and Mike's
bars put the player into weapon-safe mode through scripts), also restrict movement; the mod relaxes
that restriction.

No data change found; likely one of the Dunia.dll patches (pending trace).

## Uncertain

- Searched: every `bPullToWeaponSafe` and `SetWeaponSafeMode` reference in the changes, the
  `main_avatar.gosm.xml` / `weapons.gosm.xml` state-machine hunks (none touches safe mode), and the
  `domino/system` and `domino/user` script hunks.
- The mod still ships an earlier, scripted attempt as `_hash/4a9e3fe2.lua`, a `SetWeaponSafeMode`
  box whose header says it was superseded by a DLL patch in update 2.6 and is no longer used. It
  selected `hand_hand`, pushed the `briefing` action map and lifted weapon-safe mode again after
  5 seconds, keeping weapons blocked by the action map but freeing movement. It is stored under a
  path no script loads (vanilla's `domino/system/setweaponsafemode.lua` is unchanged), so it is
  inert; see `noise-outposts-old-weaponsafemode`.
