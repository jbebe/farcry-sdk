---
sidebar_position: 24
---

# Vehicle sound in Far Cry 3

:::info[Verified via reverse engineering]
Traced in retail Far Cry 3's `FC3.dll` (Ghidra program `/FarCry3-Retail`), which has no symbols;
the function names below are ours. Retail values come from
`worlds\fc3_main\generated\entitylibrary_full.fcb`, unpacked and converted with FCBConverter. Nothing
here was heard or measured in game.
:::

In Far Cry 3 the engine sound follows the car's physics. Far Cry 2 [makes its RPM up](./audio-runtime.md#the-rpm-is-emulated-from-speed)
from road speed over three pretend gears. Far Cry 3 takes it from the physics, whose gearbox has real
gears, shift points and clutch time. The sound smooths that RPM once more and gets two extra signals
about gear changes: one when a gear engages, one while a change is under way.

## The physics RPM

Cars are still the Havok vehicle kit, as in [Far Cry 2](./vehicle-physics.md). Far Cry 3 replaces the
transmission and the engine with its own subclasses, and builds both from the archetype's
`WheeledParams` instead of constants in code.

### Every step

`VehicleTransmission_CalcTransmission` (`0x11384CE8`) works out the RPM in
`VehicleTransmission_CalcDampedRPM` (`0x11384B84`):

```
wheel = Havok calcRPM                     // driven wheels' spin in rpm × current gear ratio, never below 0
if the clutch is out (a gear change is running):
    rpm = rpm − RpmDownDamping × dt
else:
    low  = minRPM − RpmDownDamping × dt
    high = min(max(rpm, low) + RpmUpDamping × dt, maxRPM)
    rpm  = clamp(wheel, low, high)
rpm = max(rpm, 0)
```

What follows from it:

- **Revs climb no faster than `RpmUpDamping` per second**, so a gear that lets the wheels jump does
  not make the engine jump.
- **Revs fall to the wheels at once** while a gear is engaged. `RpmDownDamping` only limits the fall
  while the clutch is out.
- **The floor is the engine's `fEngineMinRPM`.** There is no clutch slip in this RPM: standing still
  on the throttle, it stays at idle unless the driven wheels spin. Havok's own clutch-slip rule still
  runs in the engine, but only for torque.
- **In the air or in wheelspin the revs rise**, because they follow the driven wheels, not the road.

The transmission stores the result at `+0x3C`. The engine's `calcEngineInfo`
(`VehicleEngine_CalcEngineInfo`, `0x113848D7`) copies it to its own `+0x3C`, and that copy is what
the vehicle's `GetRPM` returns to the sound.

### Gear changes

`VehicleTransmission_UpdateCurrentGear` (`0x11384DBC`) runs after the RPM. Once a gear change's
clutch time runs out, the clutch engages again. When the car is going forward with the clutch in:

- **A gear holds inside its speed window.** Each `GearsConfig` entry has `MinSpeed` and `MaxSpeed` in
  km/h. While the speed is inside the current gear's window, no shift is considered. A window with
  `MaxSpeed` 0 never holds.
- **Down**: below `fDownshiftRPM`, one gear down, with the clutch out for `fClutchDownDelayTime`. A
  further condition applies from second to first; it was not traced.
- **Up**: above `fUpshiftRPM`, moving, not in the top gear and past a check on the driven wheels that
  was not traced, one gear up with the clutch out for `fClutchDelayTime`. With `bUseTransmissionPrediction` set, the shift first predicts the RPM the new
  gear will have after the clutch time, from the speed the car is expected to lose meanwhile, scaled by
  `fTransmissionPredictionSensibility`. If that RPM is at or below `fDownshiftRPM`, the shift is
  cancelled. Without prediction the clutch engages again on the next step: the up-shift clutch time
  is only used together with prediction. Every retail car has prediction on.

Retail tuning makes the RPM fall during an up-shift to where the next gear picks up. The 4x4 shifts
up at 5,000. The clutch is out for 0.5 s at 3,500 rpm/s, so the revs fall to about 3,250, and second
gear starts at 5,000 × 1.48 ÷ 2.32 ≈ 3,190.

### From the archetype

`PhysWheeledVehicle_SetupEngine` (`0x11373DE9`) and `PhysWheeledVehicle_SetupTransmission`
(`0x113757BE`) copy `WheeledParams` into the components:

| Field | Becomes |
|---|---|
| `fEngineMinRPM`, `fEngineOptimalRPM`, `fEngineMaxRPM` | the engine's min, optimal and max RPM |
| `fEnginePower` | the engine's maximum torque |
| `fEngineCutOffSpeed`, `fEngineReverseCutOffSpeed` | no torque above these speeds |
| `GearsConfig` (`GearRatio`, `MinSpeed`, `MaxSpeed`) | the gear ratios and their speed windows |
| `fReverseGearRatio` | the reverse ratio |
| `fUpshiftRPM`, `fDownshiftRPM` | shift points |
| `fClutchDelayTime`, `fClutchDownDelayTime` | clutch time for up and down shifts |
| `RpmUpDamping`, `RpmDownDamping` | the rate limits above; 3,000 and 2,000 rpm/s by default |
| `fEngineBrakeRatio` | scales the engine's braking torque |
| `fGearBoxTopSpeed`, `fTopSpeedRPM` | the primary ratio: `fTopSpeedRPM` at `fGearBoxTopSpeed` in the top gear, on a driven wheel's real radius |

## The sound

`WheeledVehicleSound_Update` (`0x10717105`) runs every frame on a wheeled vehicle's sound component.
It calls `WheeledVehicleSound_UpdateSoundState` (`0x10715CBB`), which starts and stops events and
computes the parameters, and `WheeledVehicleSound_UpdateLastGearEmulation` (`0x10712EF5`).

### What plays when

| Moment | Events |
|---|---|
| A driver gets in | `sndEngineIgnition` |
| The engine starts | `sndPlayEngineIdleLoop`, `sndEngineLoop` and `sndExtraTorqueEngineLoop`, until it stops |
| The engine stops | the loops fade out over 0.2 s; `sndTurnOffEngine` and `sndStopEngineIdleLoop` play |
| Throttle at or above `fThrustPedalStopThreshold` | `sndThrustPedal`, stopped with a `fThrustPedalStopFadeOut` fade below it |
| Pedal below −0.5 | `sndBrake` |
| The clutch goes out for a gear change | `sndGearShift_New`, `_MinorDamage` or `_MajorDamage`, by damage state |

This is Far Cry 2's set. What changed is the gear change: it comes from the physics gearbox, and
retail Far Cry 3 fills `sndGearShift_*`.

### The RPM the sound hears

The sound does not use the physics RPM directly. `VehicleSound_FadeToward` (`0x1062F30D`) moves its
own copy toward it by at most `RpmFadeUpFactor` per second going up and `RpmFadeDownFactor` per second
going down, never past it, and keeps it between the engine's min and max RPM. The field descriptions
say rpm/s², but the code uses both as rpm/s.

### The parameters it answers

`WheeledVehicleSoundCB_GetMultiLayer` (`0x10712926`) answers the bank's
[multilayers](../file-formats/spk.md#resource-containers); what it does not know goes to the shared
`VehicleSoundCB_GetMultiLayer` (`0x1062EDCA`). The ids are the retail 4x4's.

| Field | Id | Value |
|---|---|---|
| `sndmlSpeedSoundMultilayer` | `0x00440255` | the vehicle's speed |
| `sndmlRPMSoundMultilayer` | `0x00440256` | the faded RPM above |
| `sndmlThrustPedalSoundMultilayer` | `0x0044F4D0` | the throttle, **0 to 1** (FC2 sends 0 to 100), faded in over `fPedalFadeInTime` and out over `fPedalFadeOutTime`; braking reads 0 |
| `sndmlExtraTorqueSoundMultilayer` | `0x0044025B` | the physics extra-torque factor × 100, faded by `ExtraTorqueFadeUpFactor` and `ExtraTorqueFadeDownFactor` |
| `sndmlDamageSoundMultilayer` | `0x00450C23` | a value at the vehicle's `+0xF4` × 100 |
| `sndmlStartGearSoundMultilayer` | `0x00074C81` | **new**: 1.0 when a gear engages after the clutch, then falls by `StartGearFadeDownFactor` per second |
| `sndmlGearShiftMultilayer` | `0x00177AE2` | **new**: seconds since the current gear change began, 0 when none is running |
| `sndmlWheelSlipSoundMultilayer` | `0x00440257` | the largest slip of any wheel on the ground |
| `sndmlWheelForwardSlipSoundMultilayer` | `0x00177ACF` | the largest forward slip |
| `sndmlChassisRumbleMultilayer` | `0x0017312A` | from the chassis's pitch and roll (not traced further), 0 to 1, faded by `fChassisRumbleFade` |

The speed, RPM, throttle, extra-torque, damage and wheel-slip ids are Far Cry 2's own.

### Last-gear emulation, unused

The `LastGearEmulation` block can fake gear changes in the top gear, where the real gearbox has
nothing left to shift. It is used only with `bEmulateLastGear` set, in the top gear and outside a
real gear change. The emulated RPM then climbs at `fRPMAccel` toward the max RPM. If the physics RPM
drops by more than `fRPMDecelThreshold` of the max RPM, it falls at `fRPMDecel` instead. At `fRPMMax`
it plays the gear-shift event and falls for `fRPMShiftDelay`, then restarts at `fRPMStart`. It does
this up to `MaxGear` times. `bEmulateLastGear` is off on all 55 retail wheeled archetypes.

## Retail tuning

| Archetype | Min / max RPM | Up / down shift | Clutch up / down | `RpmUp` / `RpmDownDamping` | `RpmFadeUp` / `DownFactor` | Gears (km/h window) |
|---|---|---|---|---|---|---|
| `4x4` | 1,000 / 6,000 | 5,000 / 3,600 | 0.5 / 0.1 s | 5,000 / 3,500 | 2,500 / 5,000 | 2.32 (0–39), 1.48 (27–59), 1.0 (47–75) |
| `Technical` | 1,000 / 5,700 | 4,800 / 3,200 | 0.5 / 0.1 s | 5,000 / 3,500 | 5,000 / 3,500 | 2.32 (0–35), 1.48 (27–50), 1.0 (47–75) |
| `GermanHatch` | 1,000 / 6,500 | 5,000 / 3,600 | 0.55 / 0.15 s | 6,000 / 3,500 | 6,000 / 3,500 | 2.27 (0–39), 1.48 (27–59), 1.0 (47–65) |
| `JapSedan` | 1,000 / 6,500 | 5,000 / 3,600 | 0.55 / 0.1 s | 6,000 / 3,500 | 6,000 / 3,500 | 2.27 (0–39), 1.48 (27–59), 1.0 (47–65) |
| `Buggy` | 1,000 / 7,200 | 5,999 / 3,800 | 0.26 / 0.1 s | 6,000 / 3,500 | 6,000 / 3,500 | 1.92 (0–25), 1.32 (25–40), 1.0 (40–65) |
| `Quad_Bike` | 1,200 / 6,500 | 5,000 / 3,600 | 0.35 / 0.15 s | 7,000 / 500 | 7,000 / 8,000 | 2.01 (0–30), 1.39 (30–45), 1.0 (45–65) |
| `CargoTruck` | 900 / 5,000 | 4,300 / 2,400 | 0.6 / 0.1 s | 2,500 / 2,500 | 2,500 / 2,500 | 3.22 (0–15), 2.29 (15–25), 1.84 (25–40), 1.2 (40–65) |
| `MissionSpc_APC` | 900 / 5,000 | 3,250 / 1,800 | 1.0 / 0.1 s | 8,000 / 2,500 | 2,000 / 4,500 | 2.74 (0–35), 1.92 (31–47), 1.27 (41–60), 1.0 (57–65) |

Across the cars that have sound:

- `StartGearFadeDownFactor` is 0.65, so the start-gear signal takes about 1.5 s to fade.
- `fPedalFadeInTime` and `fPedalFadeOutTime` are 0.05 s on all but one.
- `sndThrustPedal` and `sndExtraTorqueEngineLoop` are empty everywhere.
- `bUseTransmissionPrediction` is on everywhere; `fTransmissionPredictionSensibility` is 0.1–1.4.

The converted data lists one more `Gear` after the last on every car, all zero. The transmission
divides by the last gear's ratio, so the game cannot be using that entry as a gear. It was not
looked into further.

## What is not known yet

How Far Cry 3 layers its engine recordings: which loops play, and on which of the parameters above
they crossfade and pitch. The engine events point into `soundbinary\<id>.spk` banks in
`fc3_main.dat` (the 4x4's `sndEngineLoop` is `0x00052524`, which refers on to `0x00052523`). These
banks start with `04 4B 50 53`, a newer `.spk` version than the one JackAll reads.

Boats have their own `fSoundRpmFadeUpFactor` and `fSoundRpmFadeDownFactor`; their RPM was not traced.
