---
title: No automatic reload
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, player]
match:
  - "**/weaponproperties/**#**/CommonProperties/bAutoReload"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer{,/*}.xml#**/CPawn/Inventory/bAutoReload"
exclude: []
requires: []
verified: diff
---

# No automatic reload

An empty magazine is no longer replaced on its own: the player has to press reload.

## How

`bAutoReload` `True` -> `False` everywhere it is set, the two places the author's guide names
([weapons](../../../docs/docs/modding/guide/weapons.md#auto-reload)):

- `CWeaponProperties/CommonProperties/bAutoReload` on 75 weapon properties in
  `generated/entitylibrarypatchoverride.fcb` (every single-player variant, `.Mikes_Rusty`,
  `.Persistent`, `_Merc`, `.AI`, and the `.Multi` copies) and the six crossbow, sawed-off and
  silenced-shotgun properties (with their `.Multi`) in `downloadcontent/dlc1/generated/entitylibrary.fcb`.
- `CPawn/Inventory/bAutoReload` on the 13 player archetypes, `player.MainCharacter.PawnPlayer` and
  its twelve characters.

## Uncertain

- Which of the two switches the engine reads for the player's weapons (or whether both must be off)
  is not traced; the mod sets both. The `.Multi` copies only matter where this mod hands one to an
  enemy (`weapons-enemy-loadouts`, `weapons-enemy-flamethrower`).
- Not a line of the readme.
