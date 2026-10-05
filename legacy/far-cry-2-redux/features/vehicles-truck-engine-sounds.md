---
title: Big trucks get an engine sound
kind: component
bundle: fixes
status: located
systems: [audio, vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck{,/**}.xml#**/Sound/snd*"
exclude: []
requires: []
verified: diff
---

# Big trucks get an engine sound

The big trucks, silent in vanilla, play the DLC Unimog's engine sounds.

## How

`CVehicle/Sound` on `Land.BigTruck` and `Land.BigTruck.ScriptedBigTruck`, copies the mod adds to
`generated/entitylibrarypatchoverride.fcb` (redeclaring `worlds/world1`), 14 values:

| Field | Vanilla | Mod |
|---|---|---|
| `sndPlayEngineIdleLoop` | `0x0045CD73` | `0x004EE930` |
| `sndStopEngineIdleLoop`, `sndTurnOffEngine` | `0x0045CD7A` | `0x004EE933` |
| `sndEngineLoop` | `0x0045CD74` | `0x004EE931` |
| `sndEngineIgnition` | `0x0045CD79` | `0x004EE932` |
| `sndFrameLoop` | `0x004B8893` | `0x004EE940` |
| `sndThrustPedal` | `0x0045CD71` | `0x004EE92F` |

The new events are `Land.DLC_Vehicle2_DLC1`'s. This is the "Silent big truck engine" fix of
Boggalog's guide
([vehicles](../../../docs/docs/modding/guide/vehicles.md#bug-fix---silent-big-truck-engine)), with
the same values as Realism Plus's
[`vehicles-truck-engine-sounds`](../../realism-plus/features/vehicles-truck-engine-sounds.md) and
Scubrah's Patch's [`truck-engine-sounds`](../../scubrahs-patch/features/truck-engine-sounds.md).
`Land.BigTruck.A2LM09_NitrousTruck` is not copied and stays silent.
