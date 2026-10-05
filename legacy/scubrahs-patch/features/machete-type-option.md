---
title: Machete type option unlocked
kind: component
bundle: gameplay
claims:
  - "You can now change your machete type in the game options at the main menu by default"
status: located
systems: [weapons, ui, engine]
match:
  - "install/bin/dunia.dll@0x48{8f3,923}"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/bonus_content.cpp (ApplyMachetesUnlock), both builds
verified: re
---

# Machete type option unlocked

Options -> Game offers Machete Type (the Primitive and Homemade variants) without the retired
promotion that used to unlock it.

## How

One `Dunia.dll` patch. The machete ownership check (`FUN_100488d0`, UFCP's `IsMachetesUnlocked`)
opens `HKCU\Software\Ubisoft\Far Cry 2`, reads `MachetesKey` and returns 1 only if it equals 1; the
three `CFCXOptionGamePage` functions that show the option call it. The mod NOPs the three `jnz` that
leave it early - after `RegOpenKeyExA` fails, after `RegQueryValueExA` fails, and when the value is
not 1 - so it always reaches `mov bl,1`. When the key is absent the patched code queries and closes
an uninitialised handle, which fails harmlessly (inferred). UFCP does the same by making the
function return 1 at its start, which avoids that.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| open fails | `0x100488F3` | `0x100489C3` | `75 39 -> 90 90` |
| query fails / value not 1 | `0x10048923` | `0x100489F3` | `75 09 83 7C 24 04 01 75 02 -> 90 90 83 7C 24 04 01 90 90` |

Patterns (one match in each build; sites at +35 and +12):
- `83 EC 10 53 8D 44 24 08 50 68 19 00 02 00 33 DB 53 68 ?? ?? ?? ?? 68 01 00 00 80 FF 15 ?? ?? ?? ?? 85 C0 75 39`
- `89 5C 24 1C FF 15 ?? ?? ?? ?? 85 C0 75 09 83 7C 24 04 01 75 02 B3 01`