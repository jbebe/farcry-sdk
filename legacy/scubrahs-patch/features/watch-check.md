---
title: Check the watch
kind: component
bundle: gameplay
claims:
  - "Added the ability to check your watch (press 6 to toggle)"
status: located
systems: [player, input]
match:
  - "config/defaultusercontrols.xml/category_misc.xml#Control[hold_watch1]"
  - "config/inputactionmapcommon.xml/common_customwatch.xml"
  - "config/inputactionmapsingle.xml/watch_custom.xml"
  - "config/inputactionmapsingle.xml/weapons.xml#{Import[+3],Binding[+2],Binding[+3],NoResend[+2]}"
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#Group[4]/Event[{+6,+7}]"
  - "scripts/engine/objects/pawn/statemachine/main_avatar.gosm.xml#State[15]/Event[+1]"
  - "scripts/game/objects/pawn/statemachine/bedroll.gosm.xml#Group/StateRef[{+1,+2}]"
  - "scripts/game/objects/pawn/statemachine/bedroll.gosm.xml#State[6]/Event[{0,+2}]"
  - "graphics/characters/_common/animations/equipment/equipped_gadget/watch/1stge_uppb_draw_+000fw_watch_i1.mab"
  - "languages/*/oasisstrings.fragment.xml#Actions/hold_watch1"
exclude: []
requires: []
verified: diff
---

# Check the watch

Pressing 6 raises the wristwatch anywhere, not only at a bed; the player can still walk, look and
jump with it up, and 6 again puts it away.

## How

- **Key.** `inputactionmapsingle.xml` `ActionMap[weapons]` imports `common_customwatch_remap` and
  binds `kb:6` press -> `hold_watch1`, `kb:6` release -> `skip_sleep`, with `NoResend kb:6`.
  `defaultusercontrols.xml` `CATEGORY_MISC` adds the rebindable `Control[hold_watch1]` (`kb:6`,
  actionmap `common_customwatch_remap`), labelled `Actions/hold_watch1` "Toggle Watch" in all nine
  languages.
- **Raise.** `weapons.gosm.xml` `WeaponIdleGroup` gains two events on `hold_watch1`: `Select watch`
  (`CGOStateEventInventory`, `requestType` `35`) and `Push watch actionmap` (pushes
  `watch_custom`).
- **While it is up.** New `ActionMap[watch_custom]` imports `basic`, `look`, `move`, `system` and the
  new `ActionMap[common_customwatch]`, which binds `kb:space` -> `jump`, swallows `kb:f5` (quick
  save) and binds `kb:6` press -> `skip_sleep`.
- **Put away.** The bed's state machine already turns `skip_sleep` into its "don't sleep" path
  (`FadeOutNoSleep` -> `HolsterAbort`). `bedroll.gosm.xml` adds `Main Avatar/Common/xIdle` and
  `xIdleCycleBreaker` to `IdleWatchGroup`, so that path is reachable away from a bed;
  `FadeOutNoSleep` loses its `WakeUp` inventory event and gains `Pop watch actionmap`.
  `main_avatar.gosm.xml` `Swimming/HolsterSwim` also pops `watch_custom`, so entering water drops
  the watch.
- **Animation.** `1stge_uppb_draw_+000fw_watch_i1.mab` is replaced by a byte-identical copy of the
  base game's bed version, `1stge_uppb_draw_+000fw_watchbedroll_i1.mab` (2,496 -> 6,704 bytes), so
  the arm lifts the watch to the face.

## Uncertain

- Why `WakeUp` is removed is inferred: it is the bed's wake-up request, wrong when the watch is put
  away outside a bed.
- `IdleWatchGroup` also forwards `use_watch` (the sleep request) from those states; whether that can
  start sleep away from a bed is not checked.
