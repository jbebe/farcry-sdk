---
title: Buddy rescue missions optional
kind: component
bundle: gameplay
claims:
  - "Buddy rescue missions are now completely optional and will not be assigned by either faction"
  - "Fixed an issue where the game could become soft-locked after assigning a bugged buddy rescue mission"
status: located
systems: [missions, buddies, engine]
match:
  - "install/bin/dunia.dll@0x74d9d0"
  - "install/bin/dunia.dll@0x756c06"
exclude: []
requires: []
verified: re
---

# Buddy rescue missions optional

The factions no longer force a buddy rescue mission on the player as their next mission, so a
rescue that cannot be completed can no longer block the story.

## How

Two `Dunia.dll` patches skip the registration of two saved properties, each by lengthening a
`jmp +2` past one property's registration block:

- `CBuddyMission`'s property list (`FUN_1074d960`: Faction, Active, Achievement, First) loses
  **Active** (`+0x4C`). It is never read from mission data or saves, so it stays 0, and the buddy
  mission scan (`FUN_1074f510`) the selector (`FUN_10755ee0`) uses finds nothing to force-assign
  (inferred).
- `CFCXMissionManager::RegisterProperties` loses **MissionsSinceBuddyUnlock** (`+0x120`), which is
  then reset to 0 on every load; the selector offers buddy missions only once it exceeds 3.

The soft-lock fix is the same patch: with no forced assignment, no bugged rescue can be assigned.
That reading is inferred from the code, not seen in game.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| skip Active | `0x1074D9D0` | `0x107404C0` | `02 -> 4E` |
| skip MissionsSinceBuddyUnlock | `0x10756C06` | `0x10749666` | `02 -> 4A` |

Patterns (one match in each build; sites at +24 and +20):
- `C7 06 ?? ?? ?? ?? 89 5E 0C C7 46 10 ?? ?? ?? ?? C7 46 14 ?? ?? ?? ?? EB 02 33 F6 56 68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 53 6A 14 E8 ?? ?? ?? ?? 8B F0 83 C4 10 3B F3 74 2E 53 53 C7 06 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8D 4E 08 C7 46 04 ?? ?? ?? ?? E8 ?? ?? ?? ?? C7 06 ?? ?? ?? ?? C7 46 0C 4C 00 00 00 89 5E 10`
- `C7 06 ?? ?? ?? ?? 89 6E 0C C7 46 10 01 00 00 00 89 5E 14 EB 02 33 F6 56 57 E8 ?? ?? ?? ?? 53 6A 14 E8 ?? ?? ?? ?? 8B F0 83 C4 10 3B F3 74 2E 53 53 C7 06 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8D 4E 08 C7 46 04 ?? ?? ?? ?? E8 ?? ?? ?? ?? C7 06 ?? ?? ?? ?? C7 46 0C 20 01 00 00`

## Uncertain

- Mission clean-up after a buddy mission completes (map icons, `PrimaryMissionActive`) is in the
  shared `missioncompleted.lua@L32` hunk, not here.
- Dropping a property from a save's registration could also change how saves made before the mod
  load; the mod requires a new game anyway.
