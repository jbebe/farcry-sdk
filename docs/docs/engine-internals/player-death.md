---
sidebar_position: 30
---

# Player death

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`; addresses are the server's. The state machines are retail
`rescue.gosm.xml` and `main_avatar.gosm.xml`, the scripts retail Domino boxes. The two scripted
endings below were seen in game on GOG on 2026-10-20, driven by the Wildlife Overhaul prototype
`mods/wildlife-overhaul/prototype/knockout.lua`.
:::

When the player's health runs out, the game picks one of three endings: a buddy rescue, a scripted
death that a script ends by waking the player up, or a game over. All three start with the player
falling over, and a script can choose between them with three Lua calls.

## How the ending is chosen

The `die` signal sends the player's state machine to `Main Avatar/Common/Death/Die`. The rescue
state machine adds an event to that state, `try player start rescue`, a `CGOStateEventRescue` with
request type 1. It calls `CRescueManager::SetupPlayerForRescue` (`0x08969760`):

1. If the rescue manager's scripted-death flag (`+0x48`) is set, it signals `scripted_death`, or
   `scripted_death_FromVehicle` in a vehicle.
2. Otherwise `CBuddyRescue::SetupBuddyRescue` (`0x08C4F720`) asks `IsBuddyRescuePossible`. If a
   rescue is possible it signals `start_rescue`, `start_rescue_no_combat` or
   `start_rescue_FromVehicle`; if not, `no_rescue`, `no_rescue_drown` (in water) or
   `no_rescue_FromVehicle`.

Only `SetScriptedDeath` (`0x0889CEF0`) sets the flag, and only the rescue manager's constructor
clears it otherwise. When buddy rescue is possible is on [the campaign page](./campaign.md#buddies);
`BlockBuddyRescue` makes it impossible.

## The endings

| Signal | States | What the player sees |
|---|---|---|
| `start_rescue` | `Start rescue` (`Pawn_Player_Rescue_Fall`) → `WaitForEntry` → the buddy's entry, heal and exit | the buddy save |
| `no_rescue` | `FallDeadNoRescue` (`Pawn_Player_Rescue_Fall`) → `GameOver` | a fall, a fade to black, then the game-over dialog to load a save or quit |
| `scripted_death` | `FallDeadToScripted` (`Pawn_Player_Rescue_Fall`, weapon holstered) → `WaitScriptedRessurect` | a fall, a fade to black, and black until a script acts |
| `start_ragdoll` | `Death/Ragdoll` (`Pawn_Generic_Die_HitReaction`, 1.5 s) → `GuaranteedRagdoll` | the death ragdoll |

The fall is an animation, `Pawn_Player_Rescue_Fall`, for every ending. The player becomes a physics
ragdoll only on the death path: `GameOver` moves on to `Death/xRagdoll`. No retail state brings the
player back up from a ragdoll.

`FallDeadNoRescue`, `FallDeadToScripted` and both rescue starts share `DeathFallGroup`, which stops
ironsights, fades to black, lowers the sound and switches input to the cinematic action map.

### Rescue events

`CGOStateEventRescue::Activate` (`0x08B19CE0`) dispatches on the event's `requestType`:

| Request | Call |
|---|---|
| 1 | `CRescueManager::SetupPlayerForRescue` |
| 2 | `CBuddyRescue::PlayerReadyToBeRescued` |
| 3 | `CBuddyRescue::SendSetupEntry` |
| 5, 6 | `SendSetupTravel`, `SendSetupExit` |
| 7, 8 | `SendStartPlayerHeal`, `SendFinishPlayerHeal` |
| 9 | `CBuddyRescue::FinishExit` |
| 11 | `TryDropWeapon` |
| 13 | `CBuddyRescue::KillMerc` |
| 16, 17 | `StartRescueSound`, `StopRescueSound` |
| 18 | `CRescueManager::NotifyDominoPlayerDied` |
| 19 | `CRescueManager::GameOver` |
| 20 | `CRescueManager::SetupAIRagdoll` |
| 21 | `CBuddyRescue::StartDynamicFadeOut` |

`GameOver` (`0x0896A100`) sends `GREventGameOver` to the game rules, which show the game-over page.

## Waking up from a scripted death

`WaitScriptedRessurect` fires request 18. `NotifyDominoPlayerDied` (`0x08969DA0`) sends the player
entity a Domino callback event named `PlayerDied`, which the `PlayerHealthEvents` box listens for. The
state then waits for the signal `start_briefing`, which an animation played on the player through
Domino's `PlayAnim` sends. That moves on to `RemoveBlackscreenFX`, which lifts the black screen and
plays the animation in the briefing state. `DeathRessurrectGroup` heals the player and restores the
sound when it ends.

Two things are left to the script: setting scripted death off again, and handing back control. Without
the second, the player stands up and cannot move (seen in game). The GRIN side missions, which wake
the player with `pawn_generic_desertgetup`, set the `default` action map before the animation, push
`cinematic`, and pop it later.

A scripted knockdown is therefore, in Lua:

1. `SetScriptedDeath(1)`, and register for `PlayerDied` on the player with `RegisterEventCallback`.
2. Kill the player, for example with `SendPierceStimNoOrigin(GetLocalPlayerId(), 32)`, twice: level
   32 is about 1,600 health, less than a full bar on Casual.
3. On `PlayerDied`, after a short delay (`CDominoDelayManager`): `SetupPlayerForAnchorAnim`, then the
   `PlayAnim` command with `pawn_generic_desertgetup`, then the
   `CFCXCountersComponentPlayerSP_HealingDone` event, then `SetScriptedDeath(0)`.
4. On the animation's `FinishedPlayAnim` event, `SetPlayerActionMap("default")`.

For a game over instead, `BlockBuddyRescue()`, kill the player, and `UnblockBuddyRescue()` a moment
later, after the ending has been chosen.

The fall and the get-up, `pawn_player_rescue_fall` and `pawn_generic_desertgetup`, are in both worlds'
depload, so neither needs loading first.

## Unknowns

- How the buddy rescue runs after `start_rescue`.
- What `HealingDone` restores, and whether the player wakes with full health.
