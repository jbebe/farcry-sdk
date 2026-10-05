---
title: Car handling - lighter cars, more steering at speed, softer brakes
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/**#**/CVehicleWheeledPhysComponent/**"
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/**#**/Sound/GearEmulation/**"
exclude:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck{,/**}.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
requires: []
verified: diff
---

# Car handling - lighter cars, more steering at speed, softer brakes

The Datsun, buggy, jeeps and Land Rovers keep more of their steering lock as they speed up; the
Datsuns, buggy and unarmed Rover are lighter and roll less, the Datsuns and buggy are faster, and
the Datsuns brake more gently. The engine note keeps rising to higher speeds. Player and AI
drivers alike.

## How

Copies the mod adds to `generated/entitylibrarypatchoverride.fcb/vehicle/land/` (redeclaring
`worlds/world1`; the vehicles world 2 also carries are identical there, so the copies apply in both
worlds), `CVehicleWheeledPhysComponent/WheeledParams` and `CVehicle/Sound/GearEmulation`, 87
values:

| Vehicle (`Land.…`) | Mass | Top speed | Roll / pitch / yaw factor | Steering `fMaxSpeed` | Brakes | Gear emulation |
|---|---|---|---|---|---|---|
| `Datsun` | 1000 -> 800 | 38 -> 45 | -> 0.2 / 0.25 / 0.22 | 12 -> 22 | 2500 -> 1200 | see below |
| `Datsun.BrokenDatsun`, `.ScriptedDatsun` | 1000 -> 900 | 38 -> 45 | -> 0.2 / 0.25 / 0.3 | 12 -> 22 | 2500 -> 1500 | see below |
| `Buggy` | 800 -> 700 | 34 -> 50 | -> 0.2 / 0.25 / 0.2 | | | |
| `JeepLiberty` | | | | 12 -> 20 | | `Gear2/fMaxSpeed` 15 -> 25 |
| `JeepLiberty.VIP` | | | | 12 -> 22 | | `Gear2/fMaxSpeed` 15 -> 25 |
| `JeepWrangler` | | | | 12 -> 22 | | `Gear2/fMaxSpeed` 14 -> 24 |
| `JeepWrangler.SE_Taxi` | | | | | rear wheels 2500 -> 1500 | |
| `Rover` | 1600 -> 1400 | | -> 0.2 / 0.25 / 0.3 | 10 -> 20 | | `Gear2/fMaxSpeed` 15 -> 35 |
| `Rover.M249_Mounted` | | | -> 0.2 / 0.35 / 0.3 | 10 -> 20 | | `Gear2/fMaxSpeed` 15 -> 25 |
| `Rover.M2_Mounted`, `.ScriptedRover` | | | | 10 -> 20 | | `Gear2/fMaxSpeed` 15 -> 25 |

Vanilla roll / pitch / yaw factors are 0.25 / 0.5 / 0.35. Brakes are every wheel's
`fBrakingTorque` unless noted. The Datsuns also change their other steering values:

| | `fLowMaxAngle` | `fHighMaxAngle` | `fLowDirectMaxAngle` | `fHighDirectMaxAngle` | `fLowSteerSpeed` | `fHighSteerSpeed` |
|---|---|---|---|---|---|---|
| vanilla | 25 | 10 | 10 | 10 | 10 | 10 |
| `Datsun` | 30 | 8 | 6 | 4 | 8 | 6 |
| `BrokenDatsun`, `ScriptedDatsun` | 36 | 6 | 8 | 6 | 8 | 6 |

and their gear emulation: `Gear0/fMaxRPM` 8000 -> 6000, `Gear1/fMaxSpeed` 11 -> 20, `Gear2/fMaxSpeed`
13 -> 40, and on the broken and scripted Datsuns `Gear2/fMinSpeed` 10.8 -> 18.

What these do ([vehicle physics](../../../docs/docs/engine-internals/vehicle-physics.md)):

- The steering narrows from the low-speed to the high-speed angles as forward speed approaches
  `fMaxSpeed`. Raising it from 10-12 to 20-22 m/s spreads that narrowing over twice the speed: at
  12 m/s (43 km/h) a held key turns the Datsun's wheels 18° instead of 10°, and the full narrowing
  arrives at 79 km/h. On the Datsun a tap turns them less (6° at rest instead of 10°).
- `fGearBoxTopSpeed` sets the transmission ratio, so it raises the top speed; lower torque factors
  make tyre forces tip and pitch the body less.
- `GearEmulation` only drives the engine sound's revs and the rev needle
  ([vehicle sounds](../../../docs/docs/engine-internals/audio-runtime.md#vehicle-sounds)): above the
  top gear's `fMaxSpeed` the revs stay flat, so these keep the note climbing with the speed.

`Land.Rover.MK19_Mounted` (world 2 only) and the DLC vehicles are not copied and keep vanilla
values. `ScriptedDatsun` is the Datsun of the opening taxi ride; its paint is
`vehicles-taxi-ride-datsun-paint`.

## Compared with other mods

Realism Plus raises engine power and top speed on every land vehicle
([`vehicles-faster-land`](../../realism-plus/features/vehicles-faster-land.md)) and leaves steering
alone. This repo's Vehicle Overhaul (`mods/vehicle-overhaul`) retunes the player's own car at
runtime, a wider steering lock among its changes, and leaves AI drivers on retail values.

## Uncertain

- The Datsun angles are computed from the documented steering formula, not seen in game.
