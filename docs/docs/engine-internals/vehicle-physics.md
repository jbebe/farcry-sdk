---
sidebar_position: 23
---

# Vehicle physics

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (symbols) and mapped into `Dunia.dll` (Steam v1.03). Addresses without a
binary named are Steam `Dunia.dll`. Vehicle Overhaul (`mods/vehicle-overhaul`) drives everything
below on the player's car, and its speed, braking, grip and centre-of-mass changes were felt in game.
:::

Cars are the Havok vehicle kit, assembled in code from the archetype's `WheeledParams`. The `.hkx`
holds only bodies, shapes and hinges. FC2 replaces four Havok components with its own subclasses and
leaves the transmission, brake and velocity damper as Havok made them. Boats and the paraglider are
plain `hkpUnaryAction`s and do not use the kit.

## Building a car

`CPhysWheeledVehicleEntityImpl::BuildVehicle` (`0x104ACA30`) allocates the components and runs
`SetupVehicleData` (`0x104AB610`), then one `SetupComponent` per component, then `hkpVehicleInstance::init`.

| Archetype field | Becomes | Note |
|---|---|---|
| `fEnginePower` | engine `maxTorque` | peak torque in N·m at 5,500 rpm, not power |
| `fGearBoxTopSpeed`, `nGears` | `primaryTransmissionRatio` | `calculatePrimaryTransmissionRatio(fGearBoxTopSpeed, 0.4, 7500, topGear)`: the speed is in mph and the wheel radius a fixed 0.4 m |
| `fTorqueRoll/Pitch/YawFactor` | `hkpVehicleData` `+0x60/+0x64/+0x68` | how much tyre forces roll, pitch and yaw the chassis; 1 is physical |
| `fChassisUnitInertia*` | `+0x74/+0x78/+0x7C` | |
| Wheel `fBrakingTorque` | brake `maxBreakingTorque` | |
| Wheel `fSuspStrength`, damping | suspension spring parameters | per kilogram of chassis mass |
| Wheel `fSuspLength` | suspension length | |
| `fMass`, `vectorCenterOfMassOffset` | the chassis body | the centre of mass is the shape's, plus the offset |

Fixed in code, out of the archetype's reach:

- **Engine**: 1,000 / 5,500 / 7,500 rpm, torque factor 0.8 at minimum and 0 at maximum rpm,
  resistance 0.05 / 0.1 / 0. Gears 1.5, 1.2, 0.9, 0.75, then each 0.75 of the last; shifts at
  6,500 up and 3,500 down; reverse 1.75.
- **Tyres**: friction **3.0**, at most 6.0; width 0.2. The radius is half the wheel body's height in
  the `.hkx`.
- **Brakes**: a full pedal locks a wheel only after `wheelsMinTimeToBlock`, set to **1,000 s**, so the
  wheels never lock.
- **Aerodynamics**: air density 1.3, frontal area 1, drag 0.7, lift −0.3, and `extraGravity`
  (0, 0, **−5**): five more m/s² of downward pull on every car.
- **Velocity damper**: spin damping 1.0 under 4 rad/s, which stops slow spins.
- Only wheels 0 and 1 steer.

`hkpVehicleData::init` (`0x10BF9C70`) derives the chassis's response to tyre forces at `+0x180`:
(pitch, roll, yaw factor ÷ unit inertia) ÷ mass. It also derives the friction solver's lever arms
from the chassis centre of mass and each wheel's axle: its hardpoint plus the suspension direction
times the suspension length. Running it again after moving the centre of mass or changing a
suspension length rebuilds both.

## Every step

`CHkPhysVehicleInstance::applyAction` (`0x104AC570`) runs `CPhysWheeledVehicleEntityImpl::Action`
(`0x104AC3A0`) and then Havok's own step. `Action` applies rolling resistance and sets the engine's
peak torque:

```
maxTorque = (enginePower + fExtraClimbEnginePower × max(0, nose-up)) × enginePowerScale
```

`enginePower` is `Impl + 0x1C4`, the climb power `+0x1C8`, the scale `+0xCC`. The climb term is the
archetype's assist for hills: 400 on top of the Rover's 95.

**Rolling resistance** (`0x104ABF70`): above 1 m/s, each wheel pushes back by the ground's
`fRollResist` times its suspension force. Retail `physicmaterial.xml` has 0.1 everywhere, about
1.5 m/s² on a coasting car.

**Speed limiter**: `CHkPhysVehicleAerodynamics::calcAerodynamics` (`0x104C7FB0`) adds
`10 × mass × (v − 16.667)²` against the horizontal velocity above 16.667 m/s when aerodynamics `+0x30`
is set. That is a hard ceiling at about 60 km/h. `SetDriver` sets the flag for a human driver or none,
and clears it for AI.

**Grip**: the wheel collide's ground-friction override (`0x104C7520`) takes the ground's tyre friction
from `physicmaterial.xml` and multiplies it by the wheel's 3.0. The handbrake lowers it on the rear
wheels while steering. On slopes it fades from full at `fGroundFrictionReduceMinAngle` to nothing at
`MaxAngle`.

## Driver input

`SVehicleImpl::UpdateInput` (`0x100DD9E0`) turns the controls into pedals every frame:

- Reverse while rolling forward is the brake: on a keyboard a full pedal, at once.
- Under 1.5 m/s, a pedal against the motion also pulls the handbrake.
- At a stop with no pedal, the handbrake holds the car.
- Reverse throttle is all or nothing.

`fAccelerationPushFactor` only moves the driver's view with acceleration. `fJumpOutBrakeFactor` is
the brake pedal left on when the driver gets out.

## Steering

`CHkPhysVehicleSteering::calcMainSteeringAngle` (`0x104C7340`) replaces Havok's steering. It turns
the front wheels by speed and by how long the key is held, and knows nothing of the tyres' grip:

- `t` is the forward speed over the steering's `fMaxSpeed`, at most 1. It has no lower bound, so it
  goes negative in reverse.
- An input under 0.7 sets the angle at once, up to lerp(`fLowDirectMaxAngle`, `fHighDirectMaxAngle`, t).
- From 0.7 up, as a held key is, the angle starts from the direct one and winds out at
  lerp(`fLowSteerSpeed`, `fHighSteerSpeed`, t) per second to lerp(`fLowMaxAngle`, `fHighMaxAngle`, t).
- An AI driver's input sets the angle straight to the maximum times the input. With timed steering
  off, the steering is Havok's own, which narrows the angle by (`fMaxSpeedFullSteeringAngle` / v)²
  above that speed.

The Datsun's wheels turn 10° at once and wind out at 10°/s, to 25° standing still and 10° from 12 m/s
(43 km/h) up, so at speed a held key gets no more than a tap.

## Who is driving

`CPhysWheeledVehicleEntityImpl::SetDriver(int)` (no function start in the address library; found by
pattern) is told 0 for no driver, 1 for a human and 2 for AI. It sets the speed limiter and the steering
mode, and resets the AI's boost multipliers, `SetSpecialEnginePowerMultiplier` (`0x104AACF0`,
`+0x1C4 = +0x1E0 × m`) and `SetSpecialPrimaryRatioMultiplier` (`0x104AAD10`).

## Layout

`CPhysWheeledVehicleEntityImpl`:

| Offset | Field |
|---|---|
| `+0x68` | chassis `hkpRigidBody` |
| `+0x184` | `hkpVehicleInstance` |
| `+0x18C` | wheel count |
| `+0x1C0` | suspension component; `+0x20` holds each wheel's last suspension force |
| `+0x1C4` / `+0x1C8` | engine power, climb power |
| `+0x1DC` / `+0x1E0` | the retail primary ratio and engine power |

`hkpVehicleInstance`: chassis `+0x18`, data `+0x1C`, steering `+0x24`, engine `+0x28`, transmission
`+0x2C` (primary ratio `+0x10`), brake `+0x30` (wheels `+0x08`, stride `0x0C`; lock time `+0x14`),
suspension `+0x34` (wheels `+0x08`, stride `0x30`, length `+0x20`; springs `+0x14`, stride `0x0C`), aerodynamics
`+0x38` (`extraGravity` `+0x20`, limiter `+0x30`), velocity damper `+0x44` (spin damping `+0x08`),
wheel infos `+0x48` (stride `0xC0`: ground friction `+0x20`, contact body `+0x24`, suspension length
`+0x50`), rpm `+0xB8`, gear `+0xCD`.

`CHkPhysVehicleSteering`: direct angles `+0x20` / `+0x24`, maximum angles `+0x28` / `+0x2C`,
`fMaxSpeed` `+0x30`, steer speeds `+0x34` / `+0x38`, timed steering off `+0x3C`, AI input `+0x3D`.

`hkpVehicleData`: wheel parameters `+0x8C` (stride `0x28`: radius `+0x00`, friction `+0x0C`, maximum
friction `+0x14`), chassis response `+0x180`, inverse mass `+0x18C`.

The chassis body's motion state starts at `+0xE0`: rotation, the swept centre of mass at `+0x120`
and `+0x130`, the local centre of mass at `+0x160`, the linear velocity at `+0x1A0`. Moving the
centre of mass is what `hkSweptTransformUtil::setCentreOfRotationLocal` does: store the new local
centre, rotate the change into world space, and add it to both swept centres.
