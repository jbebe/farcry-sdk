# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and wrecks revived as drivable vehicles. Unfinished: the camera,
the physics and the engine are in, the engine's sound for the Datsun, the big truck and the Unimog
only; shooting from the windows and the wrecks are to come.

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
  its own springs and lower gearing (1.2× its top speed). The Datsun starts as tuned in game: 80% of its
  engine's torque, 0.5 s gear changes both ways, its own lock at speed (1×), more tyre grip (0.4×), no
  rolling resistance, a little more inertia (1.02×), slightly softer springs (0.55×), and dampers at
  0.48× on compression and 0.4× on rebound. The big truck starts as a
  real empty ZIL-130: 90 km/h through its own gearbox (1.55× the top speed), 1 s gear changes, air
  brakes (0.75×), about 30° of steering lock, 2% rolling drag (0.2×), and leaf springs bouncing at
  2 Hz (0.8×) on about 15 cm of travel (1.5×), damped at 0.65× both ways. Its engine (1×) and tyre grip
  (0.6×) are as tuned in game: the real engine on the ground's own grip spun the wheels.
  - No speed limiter (retail holds a player's car near 60 km/h), longer gearing, 70% of the engine's
    torque and no climb assist.
  - Weak brakes that lock the wheels into a skid after half a second, as on a car without ABS.
  - Tyre grip that leaves the ground's own friction to decide, with no extra downforce and little
    rolling resistance, so a car coasts and slides.
  - The tyres roll, pitch and yaw the body as physics would, with no damper on spins.
  - Each car the overhaul knows carries its weight as high as the real vehicle it depicts, set from that
    vehicle's stability factor: a Land Rover Series III tips over at 0.9 g, a Datsun 1200 at 1.3 g,
    and the buggy keeps its weight at the axles. The big truck tips at 1.1 g, lower than a real ZIL-130's
    0.85, which rolled it over in left-right turns on its soft solid axles. It also carries its weight as
    far forward as an empty ZIL-130, 49% on the front axle, where the game put it far back. The Datsun
    carries 54% on its front axle, a small front-engined car's, where the game put it so far forward
    that the rear hopped about.
  - The game gives every car a small car's inertia for its weight, so the big truck rocked on its
    springs as quickly as the Datsun. It starts at 3.6 times its own, about a real ZIL-130's, so it
    leans and turns slowly.
  - The Land Rover, the big truck and the Unimog ride on solid axles, as the real ones do. Each axle's
    two wheels are sprung as one beam on springs under the chassis rails: 60% of the track apart on the
    Land Rover, half on the trucks. The beam twists under the body to keep both wheels down over ruts
    and rocks. The dampers act at the wheels, as a leaf spring's friction and shock absorbers at the
    axle's ends do, so the body stops rocking as quickly as on wheels sprung apart. The wheels hang half
    their suspension's length lower, and lean with the beam.
  - A wider steering lock: half as far again at a crawl, and twice as far at speed, where the game
    narrows it. On a car with the Datsun's 25° and 10°, that is 37.5° and 20°.
  - Softer springs, and softer dampers, softest on the rebound, so the body squats, dives and leans
    and sways before it settles.

- **A real engine, on the car you drive.** The game makes up a car's revs for its sound and rev
  counter from road speed over three pretend gears. Here they come from Havok's gearbox instead.
  - The engine idles, revs against a slipping clutch as the car pulls away, climbs to the redline in
    each gear, and runs down while the clutch is out for a change up (0.6 s).
  - Gears change as in Far Cry 3, not as Havok changes them, which is even while the clutch is out:
    never while it is out, down in 0.1 s as Far Cry 3's cars do (the Downshift time slider), and up
    only if the revs in the next gear will stay above the change-down point once the slope and the
    tyres' drag have slowed the car for the clutch time. So a car climbing a hill holds its gear
    rather than changing up and straight back down.
  - Where the slipping clutch holds the revs wanders slowly up and down, as a driver's foot does, so
    no two pull-aways sound the same.
  - Havok's engine is the same in every car; its shift point is laid over the real engine's redline.
    The Datsun runs from a 750 rpm idle to a 6,500 rpm redline, the big truck from 560 rpm to 3,200,
    the Unimog from 600 to 2,800.
  - A rev limiter cuts the drive at the redline, so no car runs on past it, in top gear or reverse.
  - The big truck drives on a ZIL-130's five-speed gearbox, from 7.45:1 down to direct, in place of the
    game's seven close gears, and keeps the game's top gear, so its top speed is unchanged. First gear
    pulls half as hard again, and each change up drops the revs to about 1,800. It changes down at
    1,300 rpm.
- **The Datsun's engine sound.** A new engine sound of an idle and seven steady-rev recordings, each
  pitched to the revs and faded into the next, a little quieter off the throttle and quieter still while
  the clutch is out. The idle plays as the idle loop, in place of retail's. A recorded start
  replaces the ignition, and on the Datsun you drive the engine fades in as the start catches, 0.8 s
  after the key. A gear-change clunk plays halfway through each shift. The new engine sound plays on
  every Datsun; the fade-in and the clunk only on the one you drive.
- **The big truck's engine sound.** The same, from a ZIL-130's own V8: an idle and seven steady-rev
  recordings from about 460 to 1,050 rpm, the idle one the lowest of them in place of retail's idle
  loop, laid over the truck's revs compressed as the Datsun's are. It has its own recorded start, which
  its engine fades in after as it catches, 0.7 s after the key; an engine-off sound where retail's
  truck stops silently; and the Datsun's clunk on gear changes.
- **The Unimog's engine sound.** The same, from an OM 352 six-cylinder diesel: an idle at about
  590 rpm and one steady take at 2,470, laid over its 600 to 2,800 rpm unpitched at their own revs, with
  its own recorded start, which its engine fades in after 0.4 s after the key. Its engine-off is the
  Datsun's.
- **Quieter tyres.** Every car's tyre sounds on the ground, dirt, gravel, grass and the rest, play
  10 dB quieter, so they no longer drown out the engine.
- **Headlamps you can see.** Every lit headlamp, on any car, shows as a bright point: a lit white
  lens from afar, never smaller than a pixel or so, and a glare that dazzles a little up close in front
  of it, narrowing to the lamp's beam. Whatever stands in the way hides it. The game lit the lenses
  of AI cars only. Headlights lighting the grass are a UFCP fix, since the game's grass is dark under
  any spot light.

The engine's `Cameras.Camera.Third` is put up and its update is taken over to place the camera. Both
are described in
[the free camera and noclip](../../docs/docs/engine-internals/free-camera-and-noclip.md). The vehicle
physics are in [vehicle physics](../../docs/docs/engine-internals/vehicle-physics.md).

## Tuning

With DevTools installed, its overlay has a **Vehicle Overhaul** window:
- the car's speed, rpm and gear;
- its centre-of-mass height, its share of the weight on the front axle, the sideways g it tips over
  at, and the g its tyres hold on the ground under them;
- a switch for the whole overhaul, one for the real engine, one for the real centre of mass, one for
  the solid axles, and a slider for every value above.

The switches and sliders keep a set of values for each real vehicle, and one for every car the overhaul
does not know. They edit the car you drive, or any car picked while you are not driving. A switch that
does nothing on the picked car, such as solid axles on a car with none, is greyed out. The values are
kept in `bin\vehicle-overhaul.ini`, a section per car.

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
  gearbox.cpp                when the car changes gear, as in Far Cry 3
  real_vehicle.cpp           the real vehicles the cars depict
  tuning.cpp                 the values, their file and the window
  sounds.cpp                 the window's Sounds tab
  lights.cpp                 the car lights, put together
  lamp_glow.cpp              each lit headlamp's bright point and glare
  shaders\                   the glow's shader
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager and the third camera's update, entities, the seat lookup,
                             terrain height, the Havok vehicle under each car, a vehicle's engine
                             sound, the sound system, sound banks, each car's headlights, the
                             frame handed to the render thread, the Direct3D device's passes
layer\mods\
  config\                    the active_camerathird control and its binding, as sections
  languages\                 the control's label, in every language
  soundbinary\               the Datsun's, the big truck's and the Unimog's engine and start sounds,
                             the truck's engine-off sound, the gear-change clunks; their pedal sounds and idles
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
