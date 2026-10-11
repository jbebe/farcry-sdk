# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and wrecks revived as drivable vehicles. Unfinished: the camera,
the physics and the engine are in, the engine's sound for the Datsun and the big truck only; shooting
from the windows and the wrecks are to come.

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
  from its own retail values, by that car's own set of tunings. Getting out puts them all back, so AI
  drivers keep the retail physics. The values below are every car's defaults. The Land Rover starts with
  its own springs and lower gearing (1.2× its top speed); the Datsun with full engine torque, 0.5 s gear
  changes, more tyre grip (0.5×) and no rolling resistance, as tuned in game.
  - No speed limiter (retail holds a player's car near 60 km/h), longer gearing, 70% of the engine's
    torque and no climb assist.
  - Weak brakes that lock the wheels into a skid after half a second, as on a car without ABS.
  - Tyre grip that leaves the ground's own friction to decide, with no extra downforce and little
    rolling resistance, so a car coasts and slides.
  - The tyres roll, pitch and yaw the body as physics would, with no damper on spins.
  - Each car the overhaul knows carries its weight as high as the real vehicle it depicts, set from that
    vehicle's stability factor: a Land Rover Series III tips over at 0.9 g, a Datsun 1200 at 1.3 g,
    and the buggy keeps its weight at the axles.
  - The Land Rover rides on solid axles, as the real one does. Each axle's two wheels are sprung as
    one beam on springs 60% of the track apart, so the beam twists under the body to keep both wheels
    down over ruts and rocks. Its wheels hang half their suspension's length lower, and lean with the
    beam.
  - A wider steering lock: half as far again at a crawl, and twice as far at speed, where the game
    narrows it. On the Datsun that is 37.5° and 20°.
  - Softer springs, and softer dampers, softest on the rebound, so the body squats, dives and leans
    and sways before it settles.

- **A real engine, on the car you drive.** The game makes up a car's revs for its sound and rev
  counter from road speed over three pretend gears. Here they come from Havok's gearbox instead.
  - The engine idles, revs against a slipping clutch as the car pulls away, climbs to the redline in
    each gear, and runs down while the clutch is out for a gear change (0.6 s).
  - Where the slipping clutch holds the revs wanders slowly up and down, as a driver's foot does, so
    no two pull-aways sound the same.
  - Havok's engine is the same in every car; its shift point is laid over the real engine's redline.
    The Datsun runs from a 750 rpm idle to a 6,500 rpm redline, the big truck from 560 rpm to 3,200.
  - A rev limiter cuts the drive at the redline, so no car runs on past it, in top gear or reverse.
- **The Datsun's engine sound.** A new engine sound of an idle and seven steady-rev recordings, each
  pitched to the revs and faded into the next, a little quieter off the throttle and quieter still while
  the clutch is out. The idle plays as the idle loop, in place of retail's. A recorded start
  replaces the ignition, and on the Datsun you drive the engine fades in as the start catches, 0.8 s
  after the key. A gear-change clunk plays halfway through each shift. The new engine sound plays on
  every Datsun; the fade-in and the clunk only on the one you drive.
- **The big truck's engine sound.** The same, from a ZIL-130's own V8: an idle and seven steady-rev
  recordings from about 460 to 1,050 rpm, the idle one the lowest of them in place of retail's idle
  loop, laid over the truck's revs compressed as the Datsun's are. It has its own recorded start, an
  engine-off sound where retail's truck stops silently, and the Datsun's clunk on gear changes. The
  DLC's big truck, in the DLC's multiplayer maps, keeps its own.
- **Quieter tyres.** Every car's tyre sounds on the ground, dirt, gravel, grass and the rest, play
  10 dB quieter, so they no longer drown out the engine.

The engine's `Cameras.Camera.Third` is put up and its update is taken over to place the camera. Both
are described in
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md). The vehicle
physics are in [vehicle physics](../../docs/docs/engine-internals/vehicle-physics.md).

## Tuning

With DevTools installed, its overlay has a **Vehicle Overhaul** window:
- the car's speed, rpm and gear;
- its centre-of-mass height, the sideways g it tips over at, and the g its tyres hold on the ground
  under them;
- a switch for the whole overhaul, one for the real centre of mass, one for the solid axles, and a
  slider for every value above.

The sliders keep a set of values for each real vehicle, and one for every car the overhaul does not
know. They edit the car you drive, or any car picked while you are not driving. The values are kept in
`bin\vehicle-overhaul.ini`, a section per car.

Its **Sounds** tab switches each of the car's sounds off and on, to hear what makes which noise. Its
sliders set how loud each sound type plays while driving, for the session only.

## Layout

```
src\                         the FCSE plugin
  third_person.cpp           the toggle, and putting the first-person camera back
  chase.cpp                  where the chase camera goes each frame
  physics.cpp                the player's car tuned and put back, and the real vehicles' weight
  axles.cpp                  the solid axles: springs, dampers and leaning wheels
  drivetrain.cpp             the engine's revs and gear changes, for the sound and rev counter
  real_vehicle.cpp           the real vehicles the cars depict
  tuning.cpp                 the values, their file and the window
  sounds.cpp                 the window's Sounds tab
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager and the third camera's update, entities, the seat lookup,
                             terrain height, the Havok vehicle under each car, a vehicle's engine
                             sound, the sound system, sound banks
layer\mods\
  config\                    the active_camerathird control and its binding, as sections
  languages\                 the control's label, in every language
  soundbinary\               the Datsun's and the big truck's engine and start sounds, the truck's
                             engine-off sound, the gear-change clunks; their pedal sounds and idles
                             silenced; every car's quieter tyre sounds
  worlds\                    the big truck's archetypes in each world's entity library, which name its
                             engine-off sound
layer\plugins\vehicle-overhaul\   the built plugin, staged here by build.ps1
```

Needs FCSE 1.4.0 or later. The control sections need JackAll 1.3.0 or later.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\vehicle-overhaul\VehicleOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```
