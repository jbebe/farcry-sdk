---
title: Faster land vehicles (buggy and VIP jeep fastest)
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "{generated/entitylibrarypatchoverride.fcb,downloadcontent/dlc1/generated/entitylibrary.fcb}/vehicle/**#**/WheeledParams/{fEnginePower,fExtraClimbEnginePower,fGearBoxTopSpeed}"
  - "{generated/entitylibrarypatchoverride.fcb,downloadcontent/dlc1/generated/entitylibrary.fcb}/vehicle/**#**/Sound/GearEmulation/**"
exclude:
  # the dead placeholder copies of the DLC vehicles
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
requires: []
verified: diff
---

# Faster land vehicles (buggy and VIP jeep fastest)

Every drivable land vehicle has about a fifth more top speed and a tenth more engine; the dune buggy
and the VIP Jeep Liberty about two fifths more top speed. Player and AI drivers alike.

## How

`CVehicleWheeledPhysComponent/WheeledParams`, per archetype (engine power, climb power, top speed):

| Vehicle | `fEnginePower` | `fExtraClimbEnginePower` | `fGearBoxTopSpeed` |
|---|---|---|---|
| `Land.Rover`, `.M2_Mounted`, `.M249_Mounted`, `.MK19_Mounted` | 95 -> 105 | 400 -> 440 | 31 -> 38 |
| `Land.Datsun` | 90 -> 99 | 110 -> 121 | 38 -> 46 |
| `Land.JeepLiberty` | 95 -> 105 | 450 -> 495 | 33 -> 40 |
| `Land.JeepLiberty.VIP` | 95 -> 114 | 450 -> 540 | 33 -> 47 |
| `Land.JeepWrangler` | 88 -> 97 | 350 -> 385 | 31 -> 38 |
| `Land.Buggy` | 87 -> 105 | 175 -> 210 | 34 -> 48 |
| `Land.BigTruck`, `.A2LM09_NitrousTruck` | 150 -> 165 | 850 -> 935 | 30 -> 36 |
| `Land.DLC_Vehicle1_DLC1` (quad) | 80 -> 88 | 200 -> 220 | 41 -> 50 |
| `Land.DLC_Vehicle2_DLC1` (Unimog), `.Multi_M249_Mounted`, `.Multi_MK19_Mounted` | 88 -> 97 | 500 -> 550 | 22 -> 27 |

`CVehicle/Sound/GearEmulation` - `Gear0/fMaxSpeed`, `Gear1` and `Gear2` `fMinSpeed`/`fMaxSpeed` - go
up by the same factor as the engine (x1.1, x1.2 on the buggy and VIP jeep). The gear emulation only
drives the engine sound's revs and the rev needle
([vehicle sounds](../../../docs/docs/engine-internals/audio-runtime.md#vehicle-sounds)), so this keeps
the engine note in step with the higher speeds.

Where: the campaign vehicles are copies the mod adds to `generated/entitylibrarypatchoverride.fcb`
(redeclaring `worlds/world1` or `worlds/world2`), the DLC vehicles are edited in place in
`downloadcontent/dlc1/generated/entitylibrary.fcb`, which is the library the game reads them from.
The two multiplayer Unimog variants are the ones `patrols-vehicle-mix` puts on patrol. 120 values.

This is the "Land vehicle speed" recipe of Boggalog's guide
([vehicles](../../../docs/docs/modding/guide/vehicles.md#land-vehicle-speed)).

## Not covered

The scripted and broken variants (`Land.Datsun.ScriptedDatsun`, `.BrokenDatsun`,
`Land.Rover.ScriptedRover`, `Land.JeepWrangler.SE_Taxi`, `Land.BigTruck.ScriptedBigTruck`) and the
multiplayer variants other than the two Unimogs keep vanilla values.

## Compared with Scubrah's Patch

[`land-vehicle-speed`](../../scubrahs-patch/features/land-vehicle-speed.md) doubles engine power and
top speed on every wheeled archetype and leaves the gear emulation alone.
