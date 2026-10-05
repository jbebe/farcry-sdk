---
title: No save prompt after sleeping
kind: component
bundle: gameplay
claims:
  - "Disabled prompts to save your game after sleeping in a safe house"
status: located
systems: [player, save]
match:
  - "scripts/game/objects/pawn/statemachine/bedroll.gosm.xml#State[4]/**"
exclude: []
requires: []
verified: diff
---

# No save prompt after sleeping

Waking up from the bedroll returns control at once instead of opening the save screen.

## How

`scripts/game/objects/pawn/statemachine/bedroll.gosm.xml`, state `::Bedroll/Bedroll/States/Sleep/Savegame`
(`State[4]`), the step after `Wakeup`:

- its `Save` event (`CGOStateEventBedroll`, `requestType` `2`, triggered on begin) is removed;
- its `duration` goes `6` -> `0`, so it hands over to `xIdle` immediately instead of waiting for the
  `save_finished` / `load_finished` signals.

## Uncertain

- The other `bedroll.gosm.xml` changes (two `StateRef`s added to `IdleWatchGroup`, the `WakeUp` event
  removed from and a `Pop watch actionmap` event added to `NoSleep/FadeOutNoSleep`) serve the mod's
  watch and weapon-inspect controls, not this feature, and are left to those pages.
