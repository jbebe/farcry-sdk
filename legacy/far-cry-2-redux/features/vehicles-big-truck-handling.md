---
title: Big truck handling - heavier, softer, slower to steer, weaker brakes
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck{,/**}.xml#**/CVehicleWheeledPhysComponent/**"
exclude: []
requires: []
verified: diff
---

# Big truck handling - heavier, softer, slower to steer, weaker brakes

The big truck (the convoy truck, and its scripted variant) rolls less, rides softer and lower,
steers in smaller, slower steps and brakes half as hard.

## How

`CVehicleWheeledPhysComponent/WheeledParams` on `Land.BigTruck` and
`Land.BigTruck.ScriptedBigTruck`, copies the mod adds to `generated/entitylibrarypatchoverride.fcb`
(redeclaring `worlds/world1`), 27 values each:

| Field | Vanilla | Mod |
|---|---|---|
| `fMass` | 4000 | 4200 |
| `fExtraClimbEnginePower` | 850 | 700 |
| `fTorqueRollFactor` / `Pitch` / `Yaw` | 0.25 / 0.5 / 0.35 | 0.15 / 0.4 / 0.25 |
| `Steering/fLowDirectMaxAngle`, `fHighDirectMaxAngle` | 10 | 5 |
| `Steering/fLowSteerSpeed`, `fHighSteerSpeed` | 10 | 5 |
| every wheel `fMass` | 20 | 60 |
| every wheel `fBrakingTorque` | 4000 | 2000 |
| every wheel `fSuspStrength` | 50 | 35 |
| every wheel `fSuspLength` | 0.1 | 0.08 |
| rear wheels (`Wheel[2]`, `[3]`) `bHandBrake` | True | False |

What each field becomes in Havok is in
[vehicle physics](../../../docs/docs/engine-internals/vehicle-physics.md): the torque factors set how
far tyre forces roll, pitch and yaw the body (1 is physical), the direct angle is what a tap of the
key turns the wheels at once, the steer speed how fast a held key winds them out.

`Land.BigTruck.A2LM09_NitrousTruck` is not copied and keeps vanilla values.

## Uncertain

- The front wheels have no handbrake in vanilla, so after the change no wheel does; that a stopped
  truck then rolls on a slope is an inference from the engine's handbrake-at-rest rule.
- Wheel `fMass` is not in the documented Havok mapping; its effect is not traced.
