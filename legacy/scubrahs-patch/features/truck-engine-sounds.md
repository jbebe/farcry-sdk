---
title: Truck engine sounds
kind: component
bundle: fixes
claims:
  - "Fixed missing truck engine sounds"
status: located
systems: [audio, vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck.xml"
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck/*.xml"
exclude: []
requires: []
verified: diff
---

# Truck engine sounds

The big trucks (`Land.BigTruck`, the nitrous truck of mission A2LM09 and the scripted truck) get an
engine sound by borrowing the DLC truck's.

## How

`generated/entitylibrarypatchoverride.fcb` gains three archetypes the base game keeps only in the
world libraries: `Land.BigTruck` (`vehicle/land/bigtruck.xml`), `Land.BigTruck.A2LM09_NitrousTruck`
and `Land.BigTruck.ScriptedBigTruck`. The patch-override library is read after the world libraries,
so these copies are the ones the game uses.

Against `world1`'s `Land.BigTruck`, the engine sound fields now name the sound events of
`Land.DLC_Vehicle2_DLC1`:

| Field | Base game | Mod |
|---|---|---|
| `sndPlayEngineIdleLoop` | `0x0045CD73` | `0x004EE930` |
| `sndStopEngineIdleLoop`, `sndTurnOffEngine` | `0x0045CD7A` | `0x004EE933` |
| `sndEngineLoop` | `0x0045CD74` | `0x004EE931` |
| `sndEngineIgnition` | `0x0045CD79` | `0x004EE932` |
| `sndFrameLoop` | `0x004B8893` | `0x004EE940` |
| `sndThrustPedal` | `0x0045CD71` | `0x004EE92F` |

The DLC banks (`soundbinary/004ee930.spk`, `004ee931.spk` and siblings) ship in the DLC's
`entitylibrary` archive, beside the DLC vehicles' other resources. The campaign worlds' `depload`
lists do not name them, so they load with the DLC vehicle library (inferred, not traced).

## Shared unit

Each of the three is a whole new unit, and each also carries the mod's vehicle balancing for the
truck: part `fHealth` doubled or quadrupled, `fInitialReliability` 1 -> 2, `fEnginePower` 150 -> 300,
`fGearBoxTopSpeed` 30 -> 60, and the driver `fFOVAngle` 90 -> 100. Those belong to
`vehicle-durability`, `land-vehicle-speed` and `vehicle-fov-100`, which need this page for the big
trucks: the edits the mod makes to the trucks in `worlds/world1` and `worlds/world2` are shadowed by
these copies.

## Uncertain

- Why the base game's truck sounds fail is not traced. Their banks exist (`0045cd74.spk` holds one
  event pointing at `0x0045CD49`); the event chain is probably what breaks.
