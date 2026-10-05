---
title: Economy noise - a revert note and inert script lines
kind: noise
systems: [economy]
match:
  - "_hash/8fd1b157.bin"
  - "domino/system/givemissionreward.lua@L39"
  - "domino/user/sidemissions/convoymissions.unlockweapons.lua@L369"
exclude: []
verified: diff
---

# Economy noise - a revert note and inert script lines

Changes in the economy containers that do nothing in game. (The 49 pickup archetypes the mod re-exports into the override library unchanged are no changes at all: each is compared with the declaration it overrides.)

- **`_hash/8fd1b157.bin`.** A plain-text note from the author, stored under a hashed name in
  `patch.dat`: how to revert the diamond sound fix (delete `004f0e82.spk`, restore two `Dunia.dll`
  byte runs). Nothing reads it; see `diamond-sound-override`.
- **`domino/system/givemissionreward.lua@L39`.** The file's final line loses its line break.
- **`domino/user/sidemissions/convoymissions.unlockweapons.lua@L369`.** An earlier golden AK-47 unlock
  (`UnlockItem("goldak47 crate")`) added only as a comment; the live unlock is in
  `domino/system/missioncompleted.lua@L32` (`golden-ak47-shop`).
