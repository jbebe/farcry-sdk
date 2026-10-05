---
title: Weapon stays holstered after the game puts it away
kind: component
bundle: controls
claims:
  - "Auto holster function"
status: located
systems: [weapons, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer{,/*}.xml#**/CPawn/Inventory/bAutoDraw"
exclude: []
requires: []
verified: diff
---

# Weapon stays holstered after the game puts it away

When the game holsters the player's weapon (entering a building, climbing, leaving a vehicle), it is
not drawn again by itself: the player walks on empty-handed until drawing it.

## How

`CPawn/Inventory/bAutoDraw` `True` -> `False` on the 13 player archetypes,
`player.MainCharacter.PawnPlayer` and its twelve characters, the mod's copies in
`generated/entitylibrarypatchoverride.fcb`. This is option 1 of the author's holstering guide,
"Disabling Auto Draw", which says it is the way Redux holsters
([weapons](../../../docs/docs/modding/guide/weapons.md#option-1---disabling-auto-draw)).

## Depends on

- It is the base of the mod's holster: `input-holster-key` sends the game's own holster request,
  which would otherwise be undone by the auto draw. `weapons-fire-to-draw` and
  `weapons-grenade-redraw` add back drawing on fire, aim and after a grenade.
- Realism Plus and Scubrah's Patch holster without touching `bAutoDraw`
  ([`weapons-holster`](../../realism-plus/features/weapons-holster.md),
  [`weapon-holster`](../../scubrahs-patch/features/weapon-holster.md)).

## Uncertain

- The guide notes the change needs a new game, the `Inventory` values being saved with the player
  ([savegame](../../../docs/docs/file-formats/savegame.md)).
