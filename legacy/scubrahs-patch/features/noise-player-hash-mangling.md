---
title: Hash values mangled by the mod's converter
kind: noise
claims: []
match:
  - "**/entitylibrary*.fcb/vehicle/**#**/Parts/Part[*]/{Name,Name@type}"
  - "worlds/tmpla/generated/entitylibrary.fcb/engine/**"
  - "**/entitylibrary*.fcb/vehicle/land/jeepliberty{,/**}.xml#**/Parts/Part[{EFBFBD2EEFBFBD00,00FD2EFC}]"
  - "worlds/*/generated/*.managers.fcb/firemanager.*#hidStimEffects/StimEffects[*]/{Name,Name@type}"
  - "worlds/*/generated/*.managers.fcb/firemanager.*#hidStimEffects/StimEffects[{00C21052,5210EFBFBD00}]"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/machete_*.xml#Entity/disEntityId"
exclude: []
requires: []
verified: diff
---

# Hash values mangled by the mod's converter

Not an edit: hash fields whose bytes include one of `0x80` or above came back from the mod author's
XML round trip as text, each such byte replaced by `EF BF BD` (the UTF-8 replacement character), and
are now stored as longer `BinHex` values. `00FD2EFC` becomes `EFBFBD2EEFBFBD00`, `00C21052` becomes
`5210EFBFBD00`.

## How

- `Land.JeepLiberty` and its variants (`VIP`, `Multi`, `Multi.APR`, `Multi.Neutral`):
  `CVehicleWheeledPhysComponent/Parts/Part[3]/Name` in `worlds/world1`, `worlds/world2`, the
  override library's multiplayer variants and `worlds/tmpla` (the same mangling sits inside the
  override library's `JeepLiberty` copies). With the name corrupted the part no longer pairs with
  the base game's, so it reads as one part removed (`Part[00FD2EFC]`) and one added
  (`Part[EFBFBD2EEFBFBD00]`); any other value of that part travels inside the added one.
- `worlds/tmpla` `engine/gamemode/fcxmulti*` (the multiplayer game modes): `sVoiceOutroEventName`
  and `CommonSoundEvents/EventEntry[*]/sName`, `sChildEventName`.
- The machete copies' `disEntityId` (5462-5464 -> 5504-5506): the mod's tool renumbered the copies.
  A pick keeps the base game's id, which the copy overrides by name anyway (inferred).
- Both worlds' `FireManager`: the name hash of `Burnable.Object.StrawRoof01`.

## Uncertain

- Mangled is not harmless: a 6- or 8-byte value where the engine expects a 4-byte hash no longer
  matches its name, so the Jeep Liberty's part 3 and the straw roof's burn effect probably lose
  their lookup. Not seen in game. The `tmpla` game modes are multiplayer and editor-only.
