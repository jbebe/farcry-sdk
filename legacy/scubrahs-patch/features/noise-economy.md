---
title: Economy noise - unchanged pickup re-exports, a revert note, inert script lines
kind: noise
systems: [economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/pickups/{ammo,health}/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*/{dropped,persistent}.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/{deserteagle_new,fnfal_new,ied_new,m67,machete,molotov}.xml"
  - "_hash/8fd1b157.bin"
  - "domino/system/givemissionreward.lua@L39"
  - "domino/user/sidemissions/convoymissions.unlockweapons.lua@L369"
exclude: []
verified: diff
---

# Economy noise - unchanged pickup re-exports, a revert note, inert script lines

Changes in the economy containers that do nothing in game.

- **Pickup re-exports (49).** New fragments in `generated/entitylibrarypatchoverride.fcb/pickups/`
  that are field-for-field identical to the same archetypes in the base `worlds/world1` /
  `worlds/world2` libraries: `Ammo.Small_Ammo_Pickup`, `Small_Explosive_Pickup`, `Small_Fuel_Pickup`,
  `Health.Syrette`, `Health.WaterBottle`, every `Weapons.*.Dropped` and `Weapons.*.Persistent`, and
  `Weapons.DesertEagle_new`, `FNFAL_new`, `IED_new`, `M67`, `Machete`, `Molotov`. They came along when
  the editor exported the sibling `WeaponStorage`/`StorageRoom` pickups the armory cooldown edits
  (`fRespawnTime 0.1 -> 0`, not here). `DesertEagle_new`, `FNFAL_new` and `IED_new` exist only in the
  world2 library; declaring them for world1 too gives nothing in world1 a reason to use them.
- **`_hash/8fd1b157.bin`.** A plain-text note from the author, stored under a hashed name in
  `patch.dat`: how to revert the diamond sound fix (delete `004f0e82.spk`, restore two `Dunia.dll`
  byte runs). Nothing reads it; see `diamond-sound-override`.
- **`domino/system/givemissionreward.lua@L39`.** The file's final line loses its line break.
- **`domino/user/sidemissions/convoymissions.unlockweapons.lua@L369`.** An earlier golden AK-47 unlock
  (`UnlockItem("goldak47 crate")`) added only as a comment; the live unlock is in
  `domino/system/missioncompleted.lua@L32` (`golden-ak47-shop`).
