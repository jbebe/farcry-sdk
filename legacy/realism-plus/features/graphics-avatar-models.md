---
title: Models for the new playable characters
kind: component
bundle: gameplay
status: located
systems: [player, graphics]
match:
  - "_hash/{74d38315,9da20fb2,cae54b41,dcb50936}.xbg"
exclude: []
requires: []
verified: diff
---

# Models for the new playable characters

Four new character models, the bodies of the four characters the mod adds to the start screen
(`ui-avatar-selection`).

## How

Four whole new files, stored nameless in the patch archive; their names come from the hashes the
player archetypes use:

| File | Name | Used by |
|---|---|---|
| `_hash/74d38315.xbg` | `graphics\actors\buddy_floraguillen\floraguillen_avatar.xbg` | `MainCharacter.PawnPlayer.Flora_Guillen` |
| `_hash/cae54b41.xbg` | `graphics\actors\buddy_micheledachss\micheledachss_avatar.xbg` | `MainCharacter.PawnPlayer.Michele_Dachss` |
| `_hash/9da20fb2.xbg` | `graphics\actors\buddy_nasreendavar\nasreendavar_avatar.xbg` | `MainCharacter.PawnPlayer.Nasreen_Davar` |
| `_hash/dcb50936.xbg` | `graphics\actors\anonymous_merc\anonymous_merc.xbg` | `MainCharacter.PawnPlayer.Anonymous_Mercenary` |

The three buddy avatars (about 0.9-1.0 MB each) carry `FLORAGUILLEN_LOD0`-`LOD3` style meshes with
buddy materials. In the base game the three women's player archetypes point at
`warren_avatar.xbg`. `anonymous_merc.xbg` (4.68 MB) has the size of `merc_kit.xbg` and is a copy of
the mercenary kit model, the same kind of copy as `merc_kit_specops.xbg` and
`merc_kit_smuggler.xbg`.

## Depends on

- `player-playable-characters`, the player archetypes that load these models and their
  `PawnArchetype` entries; nothing else loads them.

## Uncertain

- Whether a buddy's third-person avatar model works as the player body in every situation (cut
  scenes, mirrors, death) is not checked.
- The other two kit copies are not here: `_hash/ef3c4043.xbg` (`merc_kit_specops`) is on
  `ai-specops-a1lm04` and `_hash/2899c2ea.xbg` (`merc_kit_smuggler`) on `patrols-convoy-smugglers`.
