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
  the `.hkx`. Viscosity friction 0.05, slip angle 0, force feedback 0.1, and a contact body may be
  pushed at up to twice gravity.
- **Friction solver**: `frictionEqualizer` **0.5**, where Havok's default is 0, so FC2 chose it.
  `maxVelocityForPositionalFriction` stays at Havok's 10.
- **Drive**: the torque is split evenly over the wheels whose `bDriving` is set, and the rest get
  none. All four of the Datsun's are set, so it is four-wheel drive at 0.25 each.
- **Axles**: wheels 0 and 1 are the front axle, 2 and 3 the rear (`PostSerialize` sets each wheel's
  axle to its index ÷ 2). `hkpVehicleData::init` takes each axle's lever point from the last wheel
  on it, not from an average.
- **Brakes**: a full pedal locks a wheel only after `wheelsMinTimeToBlock`, set to **1,000 s**, so the
  wheels never lock. The pedal input that counts as blocking is 0.9.
- **Aerodynamics**: air density 1.3, frontal area 1, drag 0.7, lift −0.3, and `extraGravity`
  (0, 0, **−5**): five more m/s² of downward pull on every car.
- **Velocity damper**: spin damping 1.0 under 4 rad/s, which stops slow spins.
- Only wheels 0 and 1 steer.

`hkpVehicleData::init` (`0x10BF9C70`) derives the chassis's response to tyre forces at `+0x180`:
(pitch, roll, yaw factor ÷ unit inertia) ÷ mass. It also derives the friction solver's lever arms
from the chassis centre of mass and each wheel's axle: its hardpoint plus the suspension direction
times the suspension length. Running it again after moving the centre of mass or changing a
suspension length rebuilds both.

The new constants above were read on GOG (`SetupVehicleData` `0x1049DA20`) against the Havok class
tables in the server's symbols **(RE-verified)**.

### What a car's model has to satisfy

The wheels are matched to the model in `SetupWheels` (GOG `0x1006EC90`, server
`CVehicleWheeledPhysComponent::SetupWheels`). Each wheel node is put in a slot by its quadrant in
model space:

| Slot | Quadrant |
|---|---|
| 0 | y ≥ 0, x ≤ 0 |
| 1 | y ≥ 0, x > 0 |
| 2 | y < 0, x ≥ 0 |
| 3 | y < 0, x < 0 |

**A car whose model breaks a rule gets no physics, and nothing says so.** `CreatePhysics` frees the
descriptor and returns without a physics entity, and neither function logs. It happens when:

- two wheels land in the same quadrant
- a wheel's body index is past the `.hkx`'s body count, which includes a rigid-body name the `.hkx`
  does not have
- there are more wheel primitives than `[Wheels]` entries, or the counts differ
- a node's position or matrix cannot be found

**The chassis body should be unrotated.** Wheel hardpoints are brought into the body's frame as
`conj(q)·p − t` and back out as `R·p + t`. These agree only when `t = R·t`, so a rotated body that is
also offset gets wrong hardpoints in Havok itself, not just wrongly drawn wheels. All of this is
**RE-verified on GOG**.

`.hkx` files are a 16-byte Dunia prefix and then a Havok 5.5.0-r1 packfile: all 2,745 retail files
carry `57E0E057 10C0C010` at `+0x10`. The loader reads from `+0x10` and refuses packfile versions
below 4.

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

The retail tyre frictions are Default 0.8, Metal 0.8, Grass 0.75, Stone 0.7, Wood 0.68 and Sand 0.65.
The normal and terrain tyres are identical, and a rigid body's friction is 0.4. So a car's grip is
3.0 × 0.65–0.8, the same front and rear, capped at 6.0.

## Engine start and fire

A car's engine starts after a delay set by its damage level (GOG `0x1006E4F0`; offsets from the
server's `RegisterProperties`) **(RE-verified)**:

| Level | Health ratio | Starts once `EngineStartTimer` passes | Power scale |
|---|---|---|---|
| 0 | above `MinorDamageLevel` | `MintEngineStartTime` | 1.0 |
| 1 | above `MajorDamageLevel` | `MinorDamageEngineStartTime` | `MinorDamageEngineScale` |
| 2 | above 0 | `MajorDamageEngineStartTime` | `MajorDamageEngineScale` |
| 3, broken | 0 | never | — |

A damaged level also needs its scale above 0, and the car needs its ignition. The scale goes to the
physics entity every frame; that it scales engine power is likely but was not traced. Retail uses
0.5 / 0.8 / 1.2 s on 19 archetypes and 0.8 / 1.2 / 1.5 s on 5. The scales are 0.66 and 0.33 on 22
archetypes.

When the engine breaks, `CVehicleTypeEngine::Update` starts a fire timer. After `fFireDelay` the
engine catches fire, and after `fExplosionDelay` more it explodes. The fire delay is 30 s and the
explosion delay 5 s on all 22 engines.

## Paint

`CVehicleMaterialComponent::ApplyChanges` (GOG `0x105487A0`) paints a car from a fixed table of **61**
colours, 0x50 bytes each **(RE-verified)**. Each entry is five colours, written to both the clean and
the broken parameter of the same name: `DiffuseColorBase`, `DiffuseColor1`, `DiffuseColor2`,
`SpecularColorBase` and `SpecularColor1`. The table is used only when `ColorOverride` is set and
`Destroyed` is not. A destroyed car gets the burnt colours whatever its override says. Entry 60 is
forced, with its own dust and dirt values, by a flag on the graphic component whose meaning is
unknown.

## Collision layers

A collision filter word holds the layer in bits 0–14 and the system group in bits 17–31. Bit 15
marks a mask rather than a layer and bit 16 pairing. Layer 3 is the character capsules. Some
queries filter by a mask of layers, layer `n` being bit `n − 1` **(RE-verified on GOG)**:

| Query | Mask | Layers |
|---|---|---|
| Bullets | `0x5BB` | 1, 2, 4, 5, 6, 8, 9, 11 — not 3 |
| AI sight | `0x5BF` | the same plus 3 |
| Wheel rays | `0x33` | 1, 2, 5, 6 |

## Mounted guns overheat

A mounted gun's heat rises by a fixed amount per bullet, up to 100 (GOG `0x100C4EC0`). At 100 its
heat state goes up and the heat resets to 0. After a delay it cools at a rate per second, and at 0 the
state goes down and the heat is set back to 100 (`0x100C4F20`). The last state locks the weapon. The
data per gun **(RE-verified)**:

| Gun | Heat per bullet | Cooling | Delay |
|---|---|---|---|
| M2 | 4 | 50 / s | 2 s |
| M249 | 2 | not checked | not checked |
| MK19 | 30, or 35 on some variants | not checked | 3 s on those variants |

## Suspension

Each wheel is a ray, cast by `CHkPhysVehicleRaycastWheelCollide::collideWheels` from the suspension's
hardpoint along its direction, for the suspension length plus the wheel radius. With no hit the
wheel has no contact and hangs at the full length. With a hit its length is the hit distance less the
radius, unclamped, so it goes negative when the ground is closer than the radius.

The spring is Havok's own. `CHkPhysVehicleSuspension::calcSuspension` (`0x104C76D0`, not marked as a
function) calls `hkpVehicleDefaultSuspension::calcSuspension` (`0x10BFA510`) and keeps the forces it
returns at `+0x20`. For each wheel in contact:

```
force = (strength × (length − current) × clippedInvContactDotSuspension − damping × closingSpeed) × chassis mass
```

The damping is the compression one while the closing speed is negative, the rebound one otherwise.
The suspension length is both the ray's reach and where the spring stops pushing, so a wheel droops
no further than the point where its force reaches zero, and a spring never pulls.

`hkpVehicleInstance::applyAction` (`0x10BF59E0`) pushes the chassis by each force along the contact
normal, at the suspension's hardpoint rather than at the contact. It solves grip per axle, not per
wheel. An axle's contact is the average of its wheels' contacts, and its friction the average of
their friction, with a wheel in the air counting as 0. Its load is the sum of their suspension
forces, and its drive and brake forces the sums of theirs. A wheel lifting off halves its axle's friction.

`CVehicleWheeledPhysComponent::UpdateWheelBonePositions` places each wheel's bone every frame. The
position is the hardpoint plus the suspension direction times the current length, from 0 up. The
rotation, from `CPhysWheeledVehicleEntityImpl::GetWheelLocalOrientation` (`0x104AB1E0`), is a fixed
turn per wheel times its spin times the steering angle, which the bone eases toward at 3 per second.
The bone is given the inverse of the model's rotation times that one. Wheels never tilt.

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
wheel infos `+0x48` (stride `0xC0`: ground friction `+0x20`, contact body `+0x24`, hardpoint `+0x30`,
ray end `+0x40`, suspension length `+0x50`, spin angle `+0xA4`), rpm `+0xB8`, gear `+0xCD`.

A wheel collide's output, one per wheel (stride `0x40`): contact point `+0x00`, normal `+0x10`,
ground friction `+0x20`, contact body `+0x24`, suspension length `+0x2C`, closing speed `+0x30`,
`clippedInvContactDotSuspension` `+0x34`.

`CHkPhysVehicleSteering`: direct angles `+0x20` / `+0x24`, maximum angles `+0x28` / `+0x2C`,
`fMaxSpeed` `+0x30`, steer speeds `+0x34` / `+0x38`, timed steering off `+0x3C`, AI input `+0x3D`.

`hkpVehicleData`: wheel parameters `+0x8C` (stride `0x28`: radius `+0x00`, friction `+0x0C`, maximum
friction `+0x14`, axle `+0x24`), chassis response `+0x180`, inverse mass `+0x18C`.

The chassis body's motion state starts at `+0xE0`: rotation, the swept centre of mass at `+0x120`
and `+0x130`, the local centre of mass at `+0x160`, the linear velocity at `+0x1A0`. Moving the
centre of mass is what `hkSweptTransformUtil::setCentreOfRotationLocal` does: store the new local
centre, rotate the change into world space, and add it to both swept centres.
