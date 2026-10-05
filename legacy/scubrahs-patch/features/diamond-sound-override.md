---
title: Diamond sound no longer drowns out other audio
kind: component
bundle: fixes
claims:
  - "Fixed an issue where the diamond update sound event would override all other audio (thanks Dzert14)"
status: located
systems: [audio, ui]
match:
  - "soundbinary/004f0e82.spk"
  - "install/bin/dunia.dll@0x80c9{98,a7}"
exclude: []
requires: []
verified: re
---

# Diamond sound no longer drowns out other audio

When the HUD diamond count changes, the game no longer starts its looping "diamond" sound, which could
get stuck over the rest of the audio. It plays the short objective sound instead, and the loop's
closing sound is silenced.

## How

The HUD (`ui\hud.mgb`) names its sounds in user data by CRC32 of a name; three matter here, resolved
against the names in `Dunia.dll`:

| Name | HUD key | Event | Vanilla bank |
|---|---|---|---|
| `SOUNDEVENT_DIAMOND_LOOP` | `C0811D49` | `0x004f0e85` | `004f0e85.spk`, a `Play` of a 12.8 KB Ogg |
| `SOUNDEVENT_DIAMOND_LOOP_END` | `902E24EF` | `0x004f0e88` | `004f0e88.spk`, a `StopNGo`: stop `0x004f0e85`, play `0x004f0e82` |
| `SOUNDEVENT_OBJECTIVE` | `294C46BA` | `0x004f014c` | `004f014c.spk` |

Two halves, as the mod's own revert note (`_hash/8fd1b157.bin`, see `noise-economy`) spells out:

- **Dunia.dll.** `CFCXMainHudUI::DoInit` looks the HUD's sounds up by name and stores the diamond
  loop's handle at `this+0x1A8`. The two pushes of the name's address `0x10EAC128`
  (`SOUNDEVENT_DIAMOND_LOOP`) become `0x10EAC0F4` (`SOUNDEVENT_OBJECTIVE`), so that slot holds the
  objective sound and the loop can never start. The `LOOP_END` slot (`+0x1C4`) is untouched.
- **Sound bank** (claimed here). `soundbinary/004f0e82.spk`, the bank holding the end sound
  `0x004f0e82` (`Play` of sample `0x004f0e81`, 13.7 KB Ogg, `-18 dB`), is replaced by a 132-byte
  byte-for-byte copy of vanilla `004f0e88.spk`. Event `0x004f0e82` no longer exists, so
  `SOUNDEVENT_DIAMOND_LOOP_END` only stops the (now never started) loop and plays nothing.

## Dunia.dll

Both operands are absolute addresses: a plugin computes the new one as the module base plus the
string's offset rather than writing the bytes.

| | Steam | GOG | Bytes |
|---|---|---|---|
| first push | `0x1080C998` | `0x107FFC18` | Steam `28 C1 -> F4 C0`, GOG `0C 3D -> D8 3C` |
| second push | `0x1080C9A7` | `0x107FFC27` | same |

Pattern (one match in each build; the sites are at +11 and +26; `??` masks addresses):
`89 5C 24 64 FF 15 ?? ?? ?? ?? 68 ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 83 C4 0C 50 68 ?? ?? ?? ?? 8D 4C 24 50 E8 ?? ?? ?? ?? 8D 96 A8 01 00 00`

## Depends on

The two halves belong together: the bank alone only mutes the closing sound and leaves the loop; the
DLL patch alone keeps the closing sound. Only the bank can travel in a layer; the DLL half is FCSE
plugin work.

## Uncertain

What "override all other audio" was exactly (a loop that never stopped, or one that ducked the mix)
is not traced; the fix removes the loop either way. The revert note is a text file stored under a
hashed name in `patch.dat` and has no effect in game.
