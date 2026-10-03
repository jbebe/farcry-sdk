# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and wrecks revived as drivable vehicles. Unfinished: the camera,
the physics and the engine are in, the engine's sound for the Datsun only; shooting from the windows
and the wrecks are to come.

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

- **Off-road physics, on the car you drive.** Every physics step, the car's Havok vehicle is re-tuned
  from its own retail values. Getting out puts them all back, so AI drivers keep the retail physics.
  - No speed limiter (retail holds a player's car near 60 km/h), longer gearing and no climb assist.
  - Weak brakes that lock the wheels into a skid after half a second, as on a car without ABS.
  - Tyre grip that leaves the ground's own friction to decide, with no extra downforce and little
    rolling resistance, so a car coasts and slides.
  - The tyres roll, pitch and yaw the body as physics would, with no damper on spins.
  - Each car the overhaul knows carries its weight as high as the real vehicle it depicts, set from that
    vehicle's stability factor: a Land Rover Series III tips over at 0.9 g, a Datsun 1200 at 1.3 g,
    and the buggy keeps its weight at the axles.
  - Softer springs and dampers.

- **A real engine, on the car you drive.** The game makes up a car's revs for its sound and rev
  counter from road speed over three pretend gears. Here they come from Havok's gearbox instead.
  - The engine idles, revs against a slipping clutch as the car pulls away, climbs to the redline in
    each gear, and runs down while the clutch is out for a gear change (0.7 s).
  - Havok's engine is the same in every car; its shift point is laid over the real engine's redline.
    The Datsun runs from a 750 rpm idle to a 6,500 rpm redline.
- **The Datsun's engine sound.** A new engine sound of five steady-rev recordings, each pitched to the
  revs and faded into the next, a little quieter off the throttle. Retail's own idle loop plays at
  idle. A recorded start replaces the ignition, and a gear-change clunk plays halfway through each
  shift. The new engine sound plays on every Datsun; the clunk only on the one you drive.

The engine's `Cameras.Camera.Third` is put up and its update is taken over to place the camera. Both
are described in
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md). The vehicle
physics are in [vehicle physics](../../docs/docs/engine-internals/vehicle-physics.md).

## Tuning

With DevTools installed, its overlay has a **Vehicle Overhaul** window:
- the car's speed, rpm and gear;
- its centre-of-mass height, the sideways g it tips over at, and the g its tyres hold on the ground
  under them;
- a switch for the whole overhaul, one for the real centre of mass, and a slider for every value
  above.

The values are kept in `bin\vehicle-overhaul.ini`.

## Layout

```
src\                         the FCSE plugin
  third_person.cpp           the toggle, and putting the first-person camera back
  chase.cpp                  where the chase camera goes each frame
  physics.cpp                the player's car tuned and put back, and the real vehicles' weight
  drivetrain.cpp             the engine's revs and gear changes, for the sound and rev counter
  real_vehicle.cpp           the real vehicles the cars depict
  tuning.cpp                 the values, their file and the window
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager and the third camera's update, entities, the seat lookup,
                             terrain height, the Havok vehicle under each car, a vehicle's engine
                             sound, sound banks
layer\mods\
  config\                    the active_camerathird control and its binding, as sections
  languages\                 the control's label, in every language
  soundbinary\               the Datsun's engine, start and gear-change sounds; its pedal sound silenced
layer\plugins\vehicle-overhaul\   the built plugin, staged here by build.ps1
```

Needs FCSE 1.3.0 or later: the dispatcher and the input pass are hooked at the same instructions as
DevTools. The control sections need JackAll 1.3.0 or later.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\vehicle-overhaul\VehicleOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```
