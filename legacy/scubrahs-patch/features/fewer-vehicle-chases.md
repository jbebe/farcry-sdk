---
title: Fewer enemies jump into vehicles to chase
kind: component
bundle: balancing
claims:
  - "Reduced the probability of enemies entering vehicles to chase the player"
status: located
systems: [ai, vehicles]
match:
  - "scripts/game/newbrains/vehiclebrain.ai.rml"
  - "engine/gamemodes/gamemodesconfig.xml#**/AdaptativeBehavior/Item[{2,9,10}]@*"
exclude: []
requires: []
verified: diff
---

# Fewer enemies jump into vehicles to chase

Vehicles ask less strongly for crews, so fewer mercs climb into nearby vehicles to give chase, and
the ones that drive are held to a speed limit.

## How

`scripts/game/newbrains/vehiclebrain.ai.rml` is replaced whole (257,990 -> 257,993 bytes). Only its
compiled half (the parameter blobs the engine loads) was hand-edited; the BlackBox source half is
unchanged.

- **Seat requests** (`CTaskVehicleSetUserRolePriority`, driver/gunner/passenger priority). The
  three blobs that ask for crew have their string tables rewritten to the same text,
  `driverPriority 3`, `gunnerPriority 1`, `passenPriority 2`, `enableSpecialMode 1`:
  - the vanilla `3/2/0` blob (PatrolPlan `NeedDriver`, SearchThreat `NeedDriverAndGunner`, Combat
    `DriverAndGunners`, Chase `NeedDriver`/`NeedDriver1`, EngageTarget `NeedDriver`);
  - the vanilla `3/2/1` blob (Return `AllowAll`, Reinforce `AcceptAll`, LeadConvoy and
    ProtectCargo `NeedDriver`);
  - the vanilla `0/3/0` "gunners only" blob (Patrol/SearchThreat/EngageTarget/ProtectCargo
    `GunnerOnly`, SearchThreat `KeepGunnerOnly`, Chase `NeedGunners`, Combat `GunnersOnly`).

  The blobs' attribute offsets were not updated for the inserted text, so JackAll's RML reader
  gets driver `3`, gunner `1`, a `passenPriority` key and a `...bleSpecialMode` fragment from the
  first two, and driver `0` plus fragments (no `gunnerPriority`) from the third. Either way no blob carries a
  `passengerPriority` key any more, and gunner priority drops from `2` to `1`.
- **Seat check** (`CTaskVehicleCheckUserPriority`, the `if_GunnerOnly` tests of TargetOnFoot and
  Chase): `gunnerPriority` `3` -> `1`, a clean in-place edit.
- **Speed limit**: `maxSpeed` `-1` (no limit) -> `15` in eight path-following blobs: Reinforce
  `FollowPath` and `FollowRoad`, LeadConvoy `FollowNormal` and `LeadNormal`, ProtectCargo
  `ApproachCargo/FollowPath`, TargetOnFoot `FollowPath`, `RamTarget/FollowPathAndStop`, Chase
  `StopAndShoot/FollowPathAndStop`.

## Depends on

The `passengerPriority` key goes with the `Dunia.dll` string edit `passengerPriority` ->
`gunnerPriority` (traced, see `patrol-every-seat`), which makes the engine read passenger
seats' priority from `gunnerPriority`.

## Uncertain

- Which of the three edits the claim is about is an inference: lower seat priorities should pull
  fewer mercs into vehicles; the 15 speed cap (units not checked) and the mangled keys may serve
  other ends.
- The blob lengths were updated but the file header's packed-half length was not (it still says
  96,504 bytes for a 96,507-byte half), so the source half now starts 3 bytes early. JackAll's
  `ai unpack` rejects the file for that; how the engine reads it - and what it does with the
  mangled keys and the 3-byte overrun - is not traced.
