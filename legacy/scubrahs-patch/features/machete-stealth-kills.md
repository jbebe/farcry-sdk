---
title: Machete stealth kills
kind: component
bundle: gameplay
claims:
  - "You can now perform stealth kills with the machete"
status: located
systems: [weapons, ai, buddies]
match:
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#State[15]/Event[2]**"
  - "scripts/game/objects/pawn/statemachine/hmr.gosm.xml#State[54]/**"
  - "_hash/{140c80b6,19509b46,5ef0e196}.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_{buddykilllistener,townkilllistener_w?}.*"
  - "domino/system/{buddyhealthevents,spawnprimarybuddy}.lua@*"
exclude: []
requires: []
verified: diff
---

# Machete stealth kills

A machete attack from behind an unaware enemy kills him outright and quietly, instead of leaving
him wounded on the ground.

## How

- **The kill.** `weapons.gosm.xml` `Machete/MeleeAttackBegin` (the start of a successful sneak
  attack) used to send the victim `HealthFailureOnOpponent` at 60% of the animation, which knocks
  him down wounded. The event is renamed `Opponent plays finish ground anim`, fires at 10-11% and
  always (`alwaysTrigger` `0` -> `1`), and sends `DoubleMacheteKillOnOpponentEventName` - the event
  the two-handed finishing move `MeleeAttackFinishDouble` already sends.
- **Silent death.** `hmr.gosm.xml` `HMR/Health/States/HMRKilledByMachete` (the victim's side):
  `DepleteHealth` moves from 45-50% to 0-1% with `triggeredOnBegin` `1`, so he dies at once, and the
  `DeathBark` event is removed, so he makes no death cry.
- **Side effects, new scripts.** Each is a whole unit run by a new omni entity in both worlds.
  - `_hash/140c80b6.lua` (`domino\User\buddykilllistener.lua`, entity
    `DominoOmniEntity_BuddyKillListener`): polls the primary and rescue buddy and logs when one dies
    from a takedown. The fix call inside it is commented out; the fix itself lives in
    `domino/system/buddyhealthevents.lua@L71`, which on death reason `-1339130170` (the machete
    takedown) calls `GetBuddiesManager():MercyKilledBuddy()` and fires `BuddyDownDied`.
    `domino/system/spawnprimarybuddy.lua@L59` stores the spawned buddy in
    `Globals.MASTER_GameGlobals.PrimaryBuddyEntityID` for the listener.
  - `_hash/19509b46.lua` / `_hash/5ef0e196.lua` (`townkilllistener_w1/w2.lua`): health listeners on
    the town (ceasefire-zone) mercenaries; any damage or death calls `ForceSocialRegionToCombat()`
    and, until the last story mission is done, `StartTownEscape()` - so a silent takedown in town
    still breaks the ceasefire.

## Depends on

- The global `PrimaryBuddyEntityID` and `FinalStoryMissionCompleted` are declared in
  `domino/user/master_gameglobals.globals.lua` (`missions-game-globals`).
- The machete weapon properties also gain `bIsSilent` `True` (in `player-machete-copies`, whole
  units shared with the range and creeping changes); whether that matters to the takedown is not
  known, so this page does not require it.

## Uncertain

- Read from the state machines; the kill, the silence and both listeners are not seen in game.
