---
title: Short buddy rescue, weapons kept
kind: component
bundle: gameplay
claims:
  - "Buddy rescues are much more fluid, faster and no longer replace your secondary weapon"
status: located
systems: [buddies, player]
match:
  - "scripts/game/objects/pawn/statemachine/rescue.gosm.xml#**"
  - "install/bin/dunia.dll@0x729360"
exclude: []
requires: []
verified: re
---

# Short buddy rescue, weapons kept

When a buddy saves the player from bleeding out in combat, the rescue plays the short version that
vanilla uses only out of combat, and the player keeps their weapons.

## How

`scripts/game/objects/pawn/statemachine/rescue.gosm.xml` has two entry paths: `BuddyRescue_StartEntry`
(rescue during combat) and `BuddyRescue_StartEntry_NoCombat`. Three connections of the combat path are
pointed at the no-combat states, which already exist in vanilla:

- `State[10]` (buddy side): `::Rescue/Rescue/Buddy/States/BlackOut1` ->
  `::Rescue/Rescue/Buddy/BlackOut_Short`;
- `State[18]` (player side): `::Rescue/Rescue/Player/States/FirstBlackout` ->
  `::Rescue/Rescue/Player/FirstBlackout_Short` (animation `pawn_buddyrescue_FirstBlackout_Short`);
- `State[38]`, on `start_rescue`: `::Rescue/Rescue/Player/States/Start rescue` ->
  `::Rescue/Rescue/Player/Start rescue NoCombat`.

`Start rescue` holsters and also fires a `try drop weapons` event (`CGOStateEventRescue`,
`requestType` `11`); `Start rescue NoCombat` only holsters. Skipping that event is what keeps the
secondary weapon.

## Dunia.dll

The same event is also disarmed in the engine, for any state machine that still sends it.
`CGOStateEventRescue::Activate` switches on the request type; entry 10 of its jump table (request type
`11`) pointed at `CGOStateEventRescue::TryDropWeapon(pawn)`, which drops the player's secondary weapon,
hands out a Makarov, Star45 or Desert Eagle by weapons-service level and refills ammo. The entry now
points at the case-13 handler, `CBuddyRescue::KillMerc(pawn)`, which finds no AI agent on the player and
does nothing (inferred). This confirms that `try drop weapons` was the swap.

| | Steam | GOG | Bytes |
|---|---|---|---|
| jump table entry | `0x10729360` | `0x1071BE50` | Steam `13 -> 08` (`0x10729313 -> 0x10729308`), GOG `03 -> F8` (`0x1071BE03 -> 0x1071BDF8`) |

The entry is an absolute address in data, so a plugin writes module base plus the handler's offset.
Find the table through the switch (one match in each build; the table address is its last four bytes):
`8B 53 10 8B 48 40 83 C2 FF 83 FA 14 0F 87 ?? ?? ?? ?? FF 24 95 ?? ?? ?? ??`
