---
title: Files nothing in single player loads
kind: noise
systems: [weapons, player, graphics]
match:
  - "_hash/{1de70519,291f00bf,42496cea,43d7bdd6,56eba71a,6d6ad4ab,9406da60,94a1ab12,b5eb4225,c208647f,d9a81cf0}.bin"
  - "_hash/{ed6daabb,fa4f4d1c}.xbg"
  - "_hash/0f59e24a.xbt"
  - "graphics/characters/buddies/warren/{warren,warren_avatar}.xbg"
exclude: []
requires: []
verified: diff
---

# Files nothing in single player loads

Sixteen whole files in the mod's `patch.dat` that no single-player archetype, animation graph or
material reaches: drafts and leftovers of the author's animation and character work.

## How

**Eleven animation copies** stored nameless (`.bin`), each a byte-identical copy of a base-game
first-person clip, under names the hashes resolve to (CRC32 of the lower-case path, matched against
candidates built from the hashlist's clip names):

| File | Name | Copy of |
|---|---|---|
| `_hash/1de70519.bin` | `weapons\secondary\desert_eagle_50\1stge_uppb_aim2iron_+000fw_sedea_i1.mab` | Desert Eagle `aim2ironcrh` |
| `_hash/d9a81cf0.bin` | `...\desert_eagle_50\1stge_uppb_aimcycle_+000fw_sedea_i1.mab` | Desert Eagle `aimcyclecrh` |
| `_hash/43d7bdd6.bin` | `weapons\primary\ithaca37\1stge_uppb_walk_+000fw_prith_i1.mab` | Ithaca `walkcrh` |
| `_hash/6d6ad4ab.bin` | `weapons\special\m1903_a4\1stge_uppb_walk_+000fw_spm19_i1.mab` | M1903 `wsafewalk` |
| `_hash/94a1ab12.bin` | `...\m1903_a4\1stge_uppb_aim2iron_+000fw_spm19_i1.mab` | M1903 `aim2ironcrh` |
| `_hash/56eba71a.bin` | `...\m1903_a4\1stge_uppb_aim_+000fw_spm19_i1.mab` | M1903 `aimcrh` |
| `_hash/291f00bf.bin` | `weapons\dlc\silencedshotgun\1stge_uppb_walk_+000fw_prsso_i1.mab` | Ithaca `walkcrh` |
| `_hash/b5eb4225.bin` | `...\silencedshotgun\1stge_uppb_aim2iron_+000fw_prsso_i1.mab` | silenced shotgun `aim2ironcrh` |
| `_hash/9406da60.bin` | `...\silencedshotgun\1stge_uppb_regular2ironsight_+000fw_prith_i1.mab` | Ithaca `aimcyclecrh` |
| `_hash/c208647f.bin` | `weapons\secondary\6p9\1stge_uppb_aimcycle_+000fw_se6p9_i1.mab` | 6P9 `aimcyclecrh` |
| `_hash/42496cea.bin` | not found | dart rifle `aim2ironcrh` |

(paths under `graphics\characters\_common\animations\`). These are the lowered-carry swaps
(`weapons-lowered-carry-animations`) aimed at clip names the weapons do not play: the Desert Eagle,
Ithaca and M1903 use `aimingcycle`/`regular2ironsight` and their walks live under
`locomotion\stand\walk\`, and the base 6P9 clip is named without `_i1`. Neither MOVE graph
(`movemgr.bin`, `dlc1.bin`) references any of the eleven hashes (`jackall-cli move validate --list`).

**Character models.**

- `graphics/characters/buddies/warren/warren.xbg` is replaced by a copy of the mod's headless Flora
  (`_hash/3b3efb2b.xbg`), and `warren_avatar.xbg` by a copy of the headless Nasreen
  (`_hash/24da6624.xbg`). In single player only the three women's player archetypes named
  `warren_avatar.xbg`, and `player-roster-women` repoints them; `warren.xbg` is named only by the
  multiplayer `PawnPlayerNetwork`. A first approach to the women's bodies, left in.
- `_hash/ed6daabb.xbg` is `graphics\characters\michele_avatar.xbg`, a copy of the mod's Michele body
  that no archetype names.

**A material and a texture.**

- `_hash/fa4f4d1c.xbg` is an `.xbm` material (stored with the wrong extension): a draft of the FAL's
  `FN_FAL_PLASTIC` with one of its two `plasticbump_01_s` -> `_02_s` switches. The live version is
  `graphics/_materials/pfernandez-m-2007050933476401.xbm` (`graphics-weapon-finishes`).
- `_hash/0f59e24a.xbt` is a byte-identical copy of `graphics/_textures/diffuse/wood/woodweapons_01_d.xbt`.

No archetype, material path in the base `.xbm`/`.xbg` files or MOVE clip names these two hashes; their
names were not found.

## Uncertain

- A sector entity or a script naming one of these files was not searched for. The `warren` models
  would show in multiplayer for the network player (out of scope).
