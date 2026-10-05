---
title: Phone calls keep playing after hanging up
kind: component
bundle: fixes
claims:
  - "Fixed the silent phone call bug (phone calls will continue playing in the background if the phone is hung up)"
status: located
systems: [missions, audio]
match:
  - "domino/system/phonecall.lua@L*"
  - "install/bin/dunia.dll@0x6ea549"
  - "install/bin/dunia.dll@0x6eaf{0e,1d}"
exclude: []
requires: [missions-game-globals]
verified: re
---

# Phone calls keep playing after hanging up

A scripted phone call's voice keeps playing if the player puts the phone away, instead of the call
going silent.

## How

`domino/system/phonecall.lua`, the `PhoneCall` Domino box (all seven hunks):

- `In()` numbers each call: it increments the global `PhoneCallInstance` and keeps its own copy;
  the engine's `PhoneCall()` is started only if the box is not already playing.
- `Event_CallPicked()` (the player answers) starts a 1.35 s delay; when it ends (`TimerDone`), and only
  if no newer call has started, the box starts sound mixing `Exclusive.Phone_Call` and plays the call's
  sound itself through `CDominoSoundManager:PlaySound` (type `6`) with a callback.
- `Event_HangUpCall()` only records `callHungUp`; the sound is no longer tied to the phone being out.
- When the sound finishes (`Callback_Sound`), the box stops the mixing and, if the phone was not hung
  up, pops the `watch_custom` action map, plays `pawn_generic_holster` and after 1.5 s holsters and
  redraws the weapon.

## Dunia.dll

The engine's own call voice is switched off, so only the script's copy plays. In
`CGadgetUsePhoneStrategy`, the event name `UpdateVoice` sends itself and the name `OnEvent` listens
for are both repointed from `"startVoice"` (`0x10E99914`) to the orphan literal `"Invalid"`
(`0x10E9996C`). The answer-call animations (`1stge_uppb_answercall*_+000fw_nowep_i1.mab`) still send
`startVoice`, which nothing now matches, and only that branch sets the voice-active flag (`+0x4A`)
`UpdateVoice` needs: the engine never starts a call voice, and so has none to cut on hang-up
(`hangupCall`, untouched). Read from the code, not heard in game.

| | Steam | GOG | Bytes |
|---|---|---|---|
| `UpdateVoice` send | `0x106EA549` | `0x106DCBB9` | Steam `14 -> 6C`, GOG `C4 15 -> 1C 16` |
| `OnEvent` match | `0x106EAF0E` | `0x106DD57E` | same |
| `OnEvent` match | `0x106EAF1D` | `0x106DD58D` | same |

The operands are pushed string addresses; a plugin computes the new one as module base plus the
string's offset. Patterns (one match in each build; `??` masks addresses):
- `UpdateVoice`, site at +13: `C6 46 7C 00 75 22 09 05 ?? ?? ?? ?? 68 ?? ?? ?? ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? E8`
- `OnEvent`, sites at +55 and +70 (shorter versions match three static string set-ups):
  `F6 05 ?? ?? ?? ?? 04 75 58 83 0D ?? ?? ?? ?? 04 8D 54 24 13 52 68 ?? ?? ?? ?? 89 2D ?? ?? ?? ?? 89 3D ?? ?? ?? ?? 88 5C 24 1B 89 1D ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 68 ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 83 C4 0C 50 68 ?? ?? ?? ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 83 C4 04 B8 08 00 00 00 84 05 ?? ?? ?? ?? 75 57 09 05 ?? ?? ?? ?? 8D 44 24 13 50 68 ?? ?? ?? ?? 89 2D ?? ?? ?? ?? 89 3D ?? ?? ?? ?? 88 5C 24 1B 89 1D ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 68 ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 83 C4 0C 50 68 ?? ?? ?? ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 83 C4 04 8B 6C 24 20 3B EB 0F 84`

## Depends on

- The two halves only work together: the DLL patch alone silences every call, the script alone plays
  each call twice.
- `missions-game-globals` declares `PhoneCallInstance`; the increment fails without it.
- `watch_custom` is the action map of the mod's watch control; popping it when absent is harmless.
