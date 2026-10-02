# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and wrecks revived as drivable vehicles. Unfinished: only the
third-person camera is started.

## What it does

- **Third-person view.** A new **Third-person view** control (V by default, rebindable in Options >
  Controls, under the vehicle controls) switches a vehicle's view to a chase camera and back. Getting
  out puts the first-person view back. Retail's leftover F2 binding sends the same signal, so F2 also
  switches.
  - The camera sits 6 m behind the car and swings after its heading with a lag. It follows the car's
    height with a lag too, so the body visibly works on its suspension.
  - The mouse orbits it all the way round, from nearly overhead to just below the car. Two seconds
    after the mouse stops, it eases back behind the car.
  - It stays above the terrain, though not out of rocks or buildings.
  - The driver is the game's first-person body: headless and posed for the first-person camera.

The engine's `Cameras.Camera.Third` is put up and its update is taken over to place the camera. Both
are described in
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md).

## Layout

```
src\                         the FCSE plugin
  third_person.cpp           the toggle, and putting the first-person camera back
  chase.cpp                  where the chase camera goes each frame
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager and the third camera's update, entities, the seat lookup,
                             terrain height
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
