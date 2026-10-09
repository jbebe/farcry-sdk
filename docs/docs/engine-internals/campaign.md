---
sidebar_position: 17
---

# The Campaign — Missions, Reputation, Buddies and Malaria

The rules the campaign runs on, as the engine applies them. Everything here was read on the GOG 1.03
`Dunia.dll`, with class and function names from the Linux server's symbols **(RE-verified)**, unless
a line says otherwise. None of it was tested in a running game.

Two related rules live elsewhere: how weapons [jam and wear
out](../modding/replacing-a-weapon.md#jamming-and-breaking), and how [mission records and layer
states travel with a save](../file-formats/savegame.md#campaign-state-outside-the-entities).

## Reputation from completed missions

`CFCXMissionManager::MissionCompleted` (`0x10745E50`) adds to the player's **base** reputation
(`SetBaseInfamyLevel`, `0x10698B70`). Some ids carry a fixed amount:

| Mission id | Reputation |
|---|---|
| `SAVE_BUDDY`, `RESCUE_REUBEN` | +5 |
| `HOUSE_CLEANING1_BASE`, `HOUSE_CLEANING2_BASE` | +10 |
| the `SUBVERT` ids, `BUDDY_BETRAYAL`, `KILL_WARLORDS` | +20 |

A library (faction) mission gives +3 while its `CompletionOrder` is below 4 and +4 after that, plus 1
outside act 1. A subverted library mission gives the same amount again.

## Diamonds are paid when a mission is accepted

The mission graphs pay a story or library mission's diamonds at acceptance, not at completion. In
`common_customboxes.missionacceptedbroadcast` (and its debug twin), the `StoryMission_Accepted` and
`LibraryMission_Accepted` broadcasts feed `GiveMissionReward` with the accepted mission id. That box
calls `CFCXMissionManager::GiveMissionReward` (`0x107435A0`), which pays the record's
`DiamondReward` through the economy component. This is read from the graph and the code; nobody has
watched it happen in game.

## Enemy loadouts follow campaign progress

`InventoryPackDifficultyLevel` decides which entries of an enemy's inventory pack it spawns with.
Every completed story or library mission raises it by 1. Buddy missions raise it only once: when the
first buddy mission of act 1 completes. The setter clamps it at 27.

On spawn, `CInventoryViewPawn::OnSpawn` hands the level to `CInventoryPack::EquipInventoryPack`. That
picks the weapon, ammo and gadget entries of the pawn's pack whose level is −1 or equals the current
level.

## Buddies

**History points.** Unlocking a buddy (`CBuddiesManager::MissionCompleted`, `0x1073D420`) gives it 3
history points, or 5 when the unlock carries a bonus. A bonus unlock also gives two random locked
buddies 3 each.

**Who is primary, and who rescues you.** Before a story mission, `0x1073CEA0` gathers the buddies
that pass three tests:

- unlocked
- belonging to the current world (act 1 is world 1; acts 2 and 3 are world 2)
- alive

It sorts them by history points, highest first. Two rules follow:

- The first becomes the primary buddy, but only when there are at least two candidates.
- The second becomes the rescue buddy only when there are more than two.

So a rescue needs three living, unlocked buddies in the world.

## Malaria

`gamemodesconfig.xml`'s `<Malaria>` block names four curves (`FirstAttackTime`,
`BetweenAttackTime`, `MinorAttackQte`, `MinorAttackDuration`), and the launcher scales the three time
curves; see the [function registry](./function-registry.md). The rest is
`CFCXCountersComponentPlayerSP`:

- **Pills.** `SetMalariaPillCount` clamps the count to 0–4, and taking a pill only decrements it
  while the count is below 4. A count of 4 is unlimited pills.
- **Minor or major.** The first attack is always minor. After that an attack is major once the
  attack count reaches `MinorAttackQte`. From act 3 a major attack is downgraded to minor.
- **No attack in a desert zone.** `CanTriggerMinorAttack` refuses any attack while the player is in
  a desert zone. A major attack there falls back to minor, which is refused as well, so the attack
  waits until the player leaves the zone. Health failure blocks both kinds.
- **A pause after dialogue.** When someone talks, attacks are frozen for 60 s (`0x1069A9B0`).
- **Vehicles.** A major attack or a blackout first takes the player out of a vehicle or off a
  mounted weapon (`0x10699600`). A minor attack does not.

## Liberating a safe house

Each safe house keeps a list of the mercenaries guarding it. `RemoveMercFromSafeHouseCheckList`
removes one, and unlocks the house when the list becomes empty. It runs in three places:

- **A guard's death** (`CPawnAgent::CleanUpOnDie`): can unlock the house.
- **A guard's despawn** (`CAIWorld::RemoveExtraAgents`): can unlock the house too.
- **Loading a save** (`PostLoad`): removes the guard without unlocking.

`UnlockSafeHouse` (`0x1068B480`) does four things:

- clears `bLocked` and sets `Discovered`
- unlocks the door
- puts the map marker in state 2 with `SafeHouse_Unlocked_GPS`
- **disables** the safe house's mission layer, switching off what that layer placed

## Unknowns

- Every rule here is read from code and data; none has been observed in a running game.
- What the buddy-unlock bonus is, in game terms.
- With a single buddy candidate, the primary buddy is set only under a further condition that was not
  traced.
