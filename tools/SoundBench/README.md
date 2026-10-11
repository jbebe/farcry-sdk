# Sound Bench

A desktop app that drives a Far Cry 2 car and plays its engine sound the way the game does, with no
game running. Pick a car, start the engine, and click **Accelerate**, **Coast** or **Brake**. Every
event the car plays is listed down to its samples, with the curve values, gains, pitch and level of
each one.

It reads the installed game as deployed, with mod layer folders on top, and plays the vehicle overhaul's
WAVs in place of the audio built from them. A WAV edited in Audacity is heard after **Refresh WAVs**, and a
bank rebuilt by the build scripts (`mods/vehicle-overhaul/assets/vehicle-sounds/build_scripts/`) after
**Reload banks**, with no deploy and no launch. Curves and gains can also be edited while the car drives,
and saved into the layer.

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
| Game, Layers, WAVs | The install (default `C:\Games\Far Cry 2`), the layer folders read over it, and the folders searched for WAVs (see [Editing the audio](#editing-the-audio)), each list separated by `;`. The defaults are this repo's `mods/vehicle-overhaul/layer` and `mods/vehicle-overhaul/assets/vehicle-sounds`. Kept in `%LOCALAPPDATA%\SoundBench\settings.json`. |
| Reload banks, Refresh WAVs | Reload reads every bank again, as after a build script ran. Refresh (F5) swaps in the WAVs edited since. |
| Car | Every wheeled archetype with an engine loop, one entry per sound setup, named after the real vehicle the overhaul matches it to. Archetypes that share their sounds share an entry, which drives with the first one's physics. |
| Start / Stop engine | Ignition, then the idle, engine and extra-torque loops after the archetype's start time. Stop plays the engine-off events and cuts the loops. |
| Accelerate / Coast / Brake | The pedal: W, Space and S do the same. Throttle sets how far Accelerate presses. |
| Hold gear | No gear changes (H): the revs run from idle up to the redline in the gear the car is in, and stay there on the rev limiter |
| Shift time | How long a gear change keeps the clutch out, read from the car's section of `bin\vehicle-overhaul.ini` |
| Parameters | The six values a car's multilayers ask for. **Pin** one to hold it at the slider's value whatever the drive does. The RPM slider stops at the car's redline. |
| Distance | Metres to the car, through each event's rolloff curve: 0 m is the cab, about 5 m the chase camera |
| Master, Output | The mix level, and its peak. Red means the bench itself is clipping, so lower Master. |
| Surface | The material switch value the tyres roll on |
| Voice tree | One row per event the car plays, in a fixed order, under the field that names it. A play stays after it ends, with its last values, until the next one ends. **M** mutes and **S** solos a record and everything under it. Select a record to edit its gain, a layer to edit its curves. |

Two problems show in red under a record:

- a record no loaded bank holds, which in game fails to play;
- a sample whose declared length differs from its stream.

A voice's own line also says when it is clamped at 0 dB. A positioned voice under −48 dB reads
*virtual*, as DARE drops it.

## Editing the audio

The vehicle overhaul's build scripts write every audio they build as a WAV in `game\` beside its take,
named for its record: `datsun\game\rpm-2 [0045cb51].wav` is the audio record `0x0045CB51`. The bench plays
any WAV named that way in place of the record's audio. To change one:

1. Open it in Audacity, edit it, and export it over itself as 16-bit PCM WAV. A loop repeats the whole file,
   so its end has to run into its start.
2. Press **F5** (Refresh WAVs). Each WAV that changed is encoded into its bank, which is written into the
   layer at once with nothing else in it changed, and what was playing starts again on the new audio.

A build script starts over from the takes: running it again overwrites the WAVs in `game\` and their banks.

## Editing gains

Select any record in the voice tree but an event, such as a sample, a multilayer, a switch or a random container,
and its gain shows under the tree: the dB it adds to the level of everything under it, wherever it plays.
**−1 dB** and **+1 dB** step it; a value typed in and Enter sets it. Gains save and undo as curves do.

The game clamps a voice at 0 dB, the level of its audio as it is, so no gain makes a voice louder than its
WAV: gains only lift what other gains or curves hold below that. A red note under the gain says when a
voice under the selected record is over the cap, and by how much. Past it, make the WAV itself louder.

## Editing curves

A layer's volume and pitch curves open under the voice tree when the layer is selected. The other
layers of its multilayer are drawn in grey on the same parameter, so a crossfade shows both sides. The
red line marks the parameter's value now.

| To | Do |
|---|---|
| Move a point | Drag it. With Shift held, only its value moves. A point stays between its neighbours. |
| Add a point | Double-click where it goes |
| Remove a point | Right-click it |
| Undo | Ctrl+Z, one gesture at a time |
| Zoom, pan | The wheel zooms into the parameter around the mouse; dragging the background pans |

An edit is heard from the next 10 ms block. **Save banks** writes every bank with edited curves or gains
into the layer that provides it, or into the top layer for a retail bank. Everything else in the bank is
written back byte for byte. Reloading, switching car or closing asks first when there are unsaved edits.

A build script that generates a bank, such as `datsun.py` for the Datsun's engine, regenerates its
curves and gains too and drops what was edited here.

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
  the idle and redline mapping, the clutch slip and its slow wander, and the rise and fall rates.
- **Gear changes.** The pedal reads −100 for the shift time, and the shift sound plays halfway through
  it.
- **Starting.** On a car with a catch time, the Datsun's 0.816 s or the ZIL's 0.7 s, the pedal reads −200 from the key until
  the engine catches, then comes up to the driver's over 0.5 s.
- **Real vehicles.** The table is mirrored from `real_vehicle.cpp`. Keep both mirrors in step with
  their sources.

## Where it differs from the game

- **Curves.** A curve is read linearly between points and held past its ends. That much is traced.
  How nested multilayers combine is inferred: their dB add and their pitch ratios multiply. Pitch is
  linear resampling, but DARE's own resampler and its pitch limit are not traced.
- **Havok.** A stand-in replaces Havok. It keeps FC2's engine and gearbox constants: 1000/5500/7500
  rpm, gears 1.5/1.2/0.9/0.75…, and shifts at 6500 up and 3500 down, where the overhaul's rev limiter
  also cuts the drive. It drives on flat ground, so
  there is no wheelspin, no gear hunting and no air time. How the engine resists off the pedal is an
  approximation.
- **Not emulated:**
  - DS3D panning, HRTF and DSOAL reverb;
  - occlusion and Doppler;
  - sound-type volumes and mix presets;
  - the 64-voice cap.
- **Tyres.** They roll from the moment a car is picked. Whether the game ever stops them, for example
  in the air, is not traced.
