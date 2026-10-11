# Sound Bench

A desktop app that drives a Far Cry 2 car and plays its engine sound the way the game does, with no
game running. Pick a car, start the engine, and click **Accelerate**, **Coast** or **Brake**. Every
event the car plays is listed down to its samples, with the curve values, gains, pitch and level of
each one.

It reads the installed game as deployed, with mod layer folders on top. A bank rebuilt by the vehicle
overhaul's build scripts (`mods/vehicle-overhaul/assets/vehicle-sounds/build_scripts/`) is heard after
**Reload banks**, with no deploy and no launch.

## Build and run

```
dotnet build tools/SoundBench
tools/SoundBench/bin/Debug/net10.0-windows/SoundBench.exe
```

It references `JackAll.Core` and `JackAll.Tools` for the archives, `.spk` banks, depload and the entity
library. Audio goes out through WASAPI (NAudio), and Ogg Vorbis is decoded by NVorbis.

## Using it

| Control | What it does |
|---|---|
| Game, Layers | The install (default `C:\Games\Far Cry 2`) and layer folders read over it, separated by `;`. The default layer is `mods/vehicle-overhaul/layer` in this repo. Kept in `%LOCALAPPDATA%\SoundBench\settings.json`. |
| Car | Every wheeled archetype with an engine loop, one entry per sound setup, named after the real vehicle the overhaul matches it to. Archetypes that share their sounds share an entry, which drives with the first one's physics. |
| Start / Stop engine | Ignition, then the idle, engine and extra-torque loops after the archetype's start time. Stop plays the engine-off events and cuts the loops. |
| Accelerate / Coast / Brake | The pedal: W, Space and S do the same. Throttle sets how far Accelerate presses. |
| Shift time | How long a gear change keeps the clutch out, read from the car's section of `bin\vehicle-overhaul.ini` |
| Parameters | The six values a car's multilayers ask for. **Pin** one to hold it at the slider's value whatever the drive does. |
| Distance | Metres to the car, through each event's rolloff curve: 0 m is the cab, about 5 m the chase camera |
| Master, Output | The mix level, and its peak. Red means the bench itself is clipping, so lower Master. |
| Surface | The material switch value the tyres roll on |
| Voice tree | One row per event the car plays, in a fixed order, under the field that names it. A play stays after it ends, with its last values, until the next one ends. **M** mutes and **S** solos a record and everything under it. Select a layer to plot its curves with the parameter's current value marked. |

Two problems show in red under a record:

- a record no loaded bank holds, which in game fails to play;
- a sample whose declared length differs from its stream.

A voice's own line also says when it is clamped at 0 dB. A positioned voice under −48 dB reads
*virtual*, as DARE drops it.

## What it reproduces

The game's side:

- **Bank loading.** An event's bank loads from `soundbinary\<id>.spk`, together with the banks
  world1's depload lists under it. The rolloff curves come from `2fffffff.spk`.
- **Event walk.** Every child of a multi-event or multilayer plays. A switch takes one case when its
  event starts. A random container picks by the traced rules.
- **Levels.** Each resource's gain word [5] adds in dB. A voice is clamped to −96…0 dB, then
  attenuated by the rolloff.
- **What plays when.** The engine events start and stop per the table in
  [audio-runtime.md](../../docs/docs/engine-internals/audio-runtime.md#vehicle-sounds), with the
  archetype's pedal threshold and fade.

The vehicle overhaul's side:

- **Revs.** The drivetrain is ported from `mods/vehicle-overhaul/src/drivetrain.cpp`. That code covers
  the idle and redline mapping, the clutch slip and the rise and fall rates.
- **Gear changes.** The pedal reads −100 for the shift time, and the shift sound plays halfway through
  it.
- **Real vehicles.** The table is mirrored from `real_vehicle.cpp`. Keep both mirrors in step with
  their sources.

## Where it differs from the game

- **Curves.** A curve is read linearly between points and held past its ends. That much is traced.
  How nested multilayers combine is inferred: their dB add and their pitch ratios multiply. Pitch is
  linear resampling, but DARE's own resampler and its pitch limit are not traced.
- **Havok.** A stand-in replaces Havok. It keeps FC2's engine and gearbox constants: 1000/5500/7500
  rpm, gears 1.5/1.2/0.9/0.75…, and shifts at 6500 up and 3500 down. It drives on flat ground, so
  there is no wheelspin, no gear hunting and no air time. How the engine resists off the pedal is an
  approximation.
- **Not emulated:**
  - DS3D panning, HRTF and DSOAL reverb;
  - occlusion and Doppler;
  - sound-type volumes and mix presets;
  - the 64-voice cap.
- **Tyres.** They roll from the moment a car is picked. Whether the game ever stops them, for example
  in the air, is not traced.
