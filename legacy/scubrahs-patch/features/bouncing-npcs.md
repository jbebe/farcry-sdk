---
title: No bouncing NPCs at high frame rates
kind: component
bundle: fixes
claims:
  - "Fixed the bouncing NPC bug when playing with framerates above 60 (no need to cap FPS)"
status: located
systems: [ai, buddies]
match:
  - "**#**/CharacterParams/bUseRigidBased"
exclude: []
requires: []
verified: diff
---

# No bouncing NPCs at high frame rates

Soldiers, buddies and civilians stop hopping in place when the game runs above 60 FPS. This is
scubrah's standalone "Fix Bouncing NPCs" folded in.

## How

`CCharacterPhysComponent/CharacterParams/bUseRigidBased` `True` -> `False`:

- in `worlds/world1` on 90 archetypes (21 `enemy_archetypes`, 69 `buddies`) and in `worlds/world2`
  on 93 (21 and 72) - every human character those libraries hold with the flag set. Only the
  animals (`Animals.Quadrupeds.*` and, in `world1`, `Animals.Scripted.*`) keep `True`.
- in 72 buddy and civilian copies the mod adds to `generated/entitylibrarypatchoverride.fcb` (the
  buddies, their `_Unarmed`/`_Betrayed`/`.Armed` variants, faction contacts, mission characters,
  `Civilians.Male_Civilian`, `GRIN_Operative`, the Jackal, Reuben). They differ from the world
  libraries only in this flag and `fMaxSlope` `60.000004` -> `60` (rounding), and shadow them.
- the 21 soldier copies in the override library carry `False` too; compared with the declarations they override, the flag is its own change there.

The override library loads after the world libraries, so 162 of the world-library edits are dead
in the mod: every `world2` one, and in `world1` the 53 buddies and 16 soldiers the mod also copies
into the override library (the copies, carrying the same `False`, are read instead). Picked without
the copies, those world-library edits are live again, so this page needs no other.

Checked across every library: the base game's override library and `downloadcontent/dlc1` hold no
character with the flag; `worlds/tmpla` (the editor template, not loaded in play) keeps `True` on
all 143. The new archetypes the mod adds for patrols (`Neutral_Faction.*`, `*_Patrol` civilians and
GRIN) are created with `False`.

## Uncertain

- The flag is also saved per instance in savegames
  ([savegame](../../../docs/docs/file-formats/savegame.md)), so characters already in a save may keep
  the old value; the mod asks for a new game anyway.
- That rigid-body character physics is what bounces above 60 FPS is the mod's claim; not traced.
  UFCP's processor-affinity option (`mods/UFCP`) is a workaround for the same symptom on large
  machines, not this fix.
