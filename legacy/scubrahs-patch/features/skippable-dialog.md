---
title: Skippable scripted dialog
kind: component
bundle: features
claims:
  - "Skippable Dialog: Allows you to skip scripted NPC animations & dialog by interacting with them"
status: located
systems: [missions, ai]
match:
  - "domino/system/playanim.lua@L{111,115}"
  - "domino/system/playbark.lua@L{90,95}"
  - "install/bin/dunia.dll@0x97485e"
exclude: []
requires: []
verified: re
---

# Skippable scripted dialog

While an NPC plays a scripted animation or line, pressing use on them ends it, and the mission script
carries on as if it had finished.

## How

Two stock Domino boxes that every mission graph uses:

- `domino/system/playanim.lua@L111`: when the animation starts (`Event_Started`), unless it loops or
  its file name contains `sm05_se01`, `SM16_SE01`, `cine_` or `base_Guard`, the box registers a `Use`
  callback on the pawn that fires its own `Event_FinishingSoon` and makes the pawn usable
  (`SetUsability` `true`). `@L115`, in `Event_FinishingSoon`: make the pawn unusable again and drop the
  callback.
- `domino/system/playbark.lua@L90`: when the line starts, the same `Use` callback fires the box's
  `Event_Finished`, for every pawn except entities `2054703037638782554`, `2054702832585551444` and
  `2054371512330233159`. A guard that would have kept buddy side-quest dialog at Mike's bar
  unskippable is written but switched off (`pawnChk = nil`, "apparently there is no issue with
  this"). `@L95`, in `Event_Finished`: drop the callback and make the pawn unusable.

## Dunia.dll

When an NPC's briefing starts, the engine no longer takes control away from the player. In a
`CPawnAgent` event handler (undefined in Ghidra, `0x10973C60`, slot 15 of the vtable at
`0x10EC5068`), the start branch of the event with name hash `0xD6F41533` lowers the weapon, stands the
player up (`new_stance_stand`), puts the player's component at `+0x140` into control mode 2 and has the
NPC look at the player, then sends `briefing`. The stand-up call becomes a jump straight to the
`briefing` send, so the player stays free to walk off or use the NPC (inferred).

| | Steam | GOG | Bytes |
|---|---|---|---|
| stand-up call | `0x1097485E` | `0x10963ACE` | `E8 rel32 -> EB 5D 90 90 90` |

Pattern (one match in each build; site at +16):
`8B CF E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 8B CF E8 ?? ?? ?? ?? 8B CF E8 ?? ?? ?? ?? 8B CF C7 80 A8 00 00 00 02 00 00 00 E8 ?? ?? ?? ?? C7 80 A4 00 00 00 00 00 00 00`

## Uncertain

- Which NPCs the three excluded entity ids are, and whether a skipped line's audio stops, is not
  checked.
- The DLL patch is placed here because leaving the player in control is what makes a briefing
  interruptible; which listed feature scubrah wrote it for is a guess, and the event's name behind
  hash `0xD6F41533` is unknown.
