# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and wrecks revived as drivable vehicles. Unfinished: only the
third-person camera is started.

## What it does

- **Third-person view.** A new **Third-person view** control (V by default, rebindable in Options >
  Controls, under the vehicle controls) switches a vehicle's view to the engine's own third-person
  camera and back. Getting out puts the first-person view back. Retail's leftover F2 binding sends the
  same signal, so F2 also switches. For now it is the engine's camera as it is, while how far it can
  be taken is found out.

The camera manager is described in
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md).

## Layout

```
src\                         the FCSE plugin
  third_person.cpp           the toggle, and putting the first-person camera back
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager, the seat lookup
layer\mods\
  config\                    the active_camerathird control and its binding, as sections
  languages\                 the control's label, in every language
layer\plugins\vehicle-overhaul\   the built plugin, staged here by build.ps1
```

Needs FCSE 1.3.0 or later: the dispatcher and the input pass are hooked at the same instructions as
DevTools. The control sections need JackAll 1.3.0 or later.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\vehicle-overhaul\VehicleOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```
