---
title: Withdrawn return-to-free-roam ending
kind: noise
systems: [missions]
match:
  - "domino/system/popupendofgame.lua@L*"
  - "domino/user/a3sm15_buddiesbetrayalfinalbattle.a3sm15_redemptionending.lua@L{80,81}"
  - "languages/*/oasisstrings.fragment.xml#Tutorial/END_MESSAGE"
exclude: []
verified: diff
---

# Withdrawn return-to-free-roam ending

Leftovers of a feature the mod shipped earlier and switched off in update 3.6: after the ending, a
"Thank you for playing Scubrah's Patch!" box led back into free roam. None of it runs now; the game ends
as in vanilla.

- `domino/system/popupendofgame.lua`: `@L29` comments out the `CreateTutorialMessageBox` call
  (`END_MESSAGE`, `BUTTON_CONTINUE`, callback `PlayerContinue`) next to the unchanged
  `PopUpEndOfGame()` and `Out`, with the note "Update 3.6: Disable return to free-roam to allow the
  ending scene to play out". `@L32`, `@L33`, `@L36`, `@L37` add `PlayerContinue`, which undoes the
  ending cutscene (scene participants, camera, binding, cinematic HUD, `Exclusive.ENDING` mixing,
  visibility, weapon safe mode, action map), sets sickness to `0`, sets `FinalStoryMissionCompleted`
  and teleports the player to a spawn point at 2615, 2516.6, 23.67. Nothing calls it any more.
  `@L1`, `@L18`, `@L25` drop the reflection header and empty-body comments.
- `domino/user/a3sm15_buddiesbetrayalfinalbattle.a3sm15_redemptionending.lua@L80`, `@L81`: comments
  and a commented-out rewiring that skipped the bomb animation ("Update 3.6: Revert to original").
- `Tutorial/END_MESSAGE` in all nine languages, used only by the commented-out call.

`FinalStoryMissionCompleted`, which `ceasefire-break-gunfight` reads, is therefore never set by the
mod as shipped (inference).
