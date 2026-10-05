---
title: World 2's dependency index in the editor's template world
kind: component
bundle: editor-content
status: located
systems: [engine, world]
match:
  - "worlds/tmpla/generated/tmpla_depload.dat"
exclude: []
requires: []
verified: diff
---

# World 2's dependency index in the editor's template world

The editor's template world (`tmpla`) loads the campaign's act 2 dependency index in place of its
own. That index tells the engine which materials, textures, sounds and animations each resource
pulls in.

## How

`worlds/tmpla/generated/tmpla_depload.dat` is replaced whole by a byte-for-byte copy of the base
game's `worlds\world2\generated\world2_depload.dat` (228,745 bytes, vanilla `tmpla` 202,322). One
`File` change, so it can only be taken whole.

Decoded (`jackall-cli depload decode`) and compared by resource entry against the vanilla `tmpla`
index in the worlds archive (9,130 entries; world 2 has 9,795. The patch archive's copy, which the
game reads, is 92 bytes larger and was not decoded):

- **Added, 2,603 entries**, 612 of them under `graphics\characters\_common\` (character
  animations and their sounds). There are also campaign rocks, buildings and huts, `domino\user\*`
  scripts (side missions, partner missions, debug boxes, DLC), and the dependencies of world 2's
  particle systems.
- **Removed, 1,938 entries**: dependencies of buildings, objects, fences, train pieces, sign kits and
  other props the multiplayer template uses and world 2 does not (`graphics\buildings\urban`,
  `fortification`, `industrial`, `graphics\objects\households`, `military`, `_signskit` and others).

## Depends on

- It pairs with [`editor-particles`](editor-particles.md), the same swap for the particle library.
  Each particle system's textures are listed here.
- The added character-animation entries are what AI characters placed in an editor map
  ([`ai-people-palette`](ai-people-palette.md)) would draw on.

## Uncertain

- What a missing `depload` entry costs is not established. Per the
  [weapon texturing notes](../../../docs/docs/modding/texturing-a-weapon.md), at least a texture
  reached through a listed material loads anyway. If the removed entries only drive preloading,
  dropping them is harmless. If not, some multiplayer-template objects lose their materials in the
  editor. Not checked.
- Why the author took world 2's index rather than merging the two is not known. It looks like the
  shortest way to bring in campaign content, the same as the particle library.
