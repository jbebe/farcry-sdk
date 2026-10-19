# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person camera to watch the car work, off-road
physics, an engine with real gears, and every car's engine sound recorded from a real engine.
Shooting from the windows and the wrecks revived as drivable vehicles are to come, after 1.0.

`README-release.md` is the player's version, shipped in the release zip, and `CHANGELOG.md` the
release notes; see [RELEASING.md](../../RELEASING.md).

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
  drivers keep the retail physics. The values below are the defaults for a car the overhaul does not
  know. Each car it knows starts at the tuning made for it in game, its own list in
  `src\real_vehicle.cpp`; the ZIL-130's and the Unimog's were first worked out from the real trucks
  (an empty ZIL-130 does 90 km/h through its own gearbox, a Unimog 406 71 km/h).
  - No speed limiter (retail holds a player's car near 60 km/h), longer gearing, 70% of the engine's
    torque and no climb assist.
  - Weak brakes that lock the wheels into a skid after half a second, as on a car without ABS.
  - Tyre grip that leaves the ground's own friction to decide, with no extra downforce and little
    rolling resistance, so a car coasts and slides.
  - The tyres roll, pitch and yaw the body as physics would, with no damper on spins.
  - Each car the overhaul knows carries its weight as high as the real vehicle it depicts, set by the
    sideways g it tips over at: the Datsun 1200 and the Unimog at 1.3 g, the Jeep Wrangler at 1.2, the
    Land Rover and the ZIL-130 at 1.1, the Jeep Liberty at 1.07, the quad at 1.0, and the buggy keeps
    its weight at the axles. The cars on solid axles tip higher than the real ones (a ZIL-130's 0.85),
    which rocked or rolled over on their soft springs.
  - Some carry their weight further forward or back than the game put it: the ZIL-130 49% on the
    front axle, as an empty one does, where the game put it far back; the Datsun 54%, the Wrangler 55%
    and the Liberty 57%, as small front-engined cars do. The Unimog keeps the game's 60%, as a real
    one has.
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
- **Every car's engine sound**, built from recordings of a real engine: an idle in the idle loop, the
  revs as slices of a recorded sweep (steady takes on the ZIL-130), each pitched to the revs and faded
  into the next, a little quieter off the throttle and quieter still while the clutch is out, and a
  recorded start the engine fades in after as it catches. The builds and their recordings stay local
  (`assets\vehicle-sounds\`); the layer carries the banks.

  | Car | Engine | Idle to redline | Start catches |
  |---|---|---|---|
  | Datsun 1200 | four | 750 to 6,500 rpm | 0.8 s |
  | Land Rover Series III | four | 525 to 4,000 | 0.8 s |
  | ZIL-130 | V8, its own | 560 to 3,200 | 0.7 s |
  | Unimog 406 | OM 352 six-cylinder diesel | 600 to 2,800 | 0.4 s |
  | Jeep Wrangler and the taxi | a Ram 2500's Cummins six | 750 to 3,000 | 0.6 s |
  | Jeep Liberty | a Volvo V70's diesel five | 690 to 4,800 | 0.9 s |
  | Buggy | four | 950 to 4,800 | 0.5 s |
  | Quad | four-stroke | 1,580 to 9,300 | 0.5 s |

  A gear-change clunk, recorded on the Datsun, plays halfway through each shift on the Datsun, the
  ZIL-130 and the Unimog. The ZIL-130, the Unimog and the quad play their own recorded engine-off, the
  ZIL-130's where retail's stopped silently; the other cars keep retail's. The new sounds play on every car of a kind; the fade-in and the clunk only on the one
  you drive.
- **Quieter tyres.** Every car's tyre sounds on the ground, dirt, gravel, grass and the rest, play
  10 dB quieter, so they no longer drown out the engine.
- **Headlamps you can see.** Every lit headlamp, on any car, shows as a bright point: a lit white
  lens from afar, never smaller than a pixel or so, and a glare that dazzles a little up close in front
  of it, narrowing to the lamp's beam. Whatever stands in the way hides it. The game lit the lenses
  of AI cars only. Headlights lighting the grass are a UFCP fix, since the game's grass is dark under
  any spot light.
- **New paints.** The Datsun's six paints, the Land Rover's eight and the big truck's five are new, and
  one of the buggy's, all muted. They take the game's own places in its paint table, so every car
  shows them, the ones placed in the world included. A taxi that rolls none of the Wrangler's paints
  takes the Datsun's beige.
- **The Datsun's chrome.** Its bumpers, door handles, wheel rims, grille frame and badge are chrome, in two materials of its own, where the game made them black. On the four nearest
  models; past those they are as before.
- **The big truck's grille.** Its six openings are replaced by thirteen of the user's drawing: a wide
  arched slot on top, two rows of five short eight-sided slots, and a pair below either side of a centre
  bar, with the slats that stood behind the old openings taken out. Each
  has the old ones' 29 mm wall, out to 45 m; beyond that the model's painted slots are the old six.

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
  real_vehicle.cpp           the real vehicles the cars depict, by their archetypes' iAnimVehicleType
  tuning.cpp                 the values, their file and the window
  sounds.cpp                 the window's Sounds tab
  lights.cpp                 the car lights, put together
  lamp_glow.cpp              each lit headlamp's bright point and glare
  paint.cpp                  the cars' paints, written into the game's paint table
  shaders\                   the glow's shader
  engine\                    the game's own seams: the signal dispatcher, the input pass, the camera
                             manager and the third camera's update, entities, the seat lookup,
                             terrain height, the Havok vehicle under each car, a vehicle's engine
                             sound, the sound system, sound banks, each car's headlights, the
                             paint table, the frame handed to the render thread, the Direct3D
                             device's passes
layer\mods\
  graphics\                  the big truck's model and physics, its fan a hinged body on a node of its own
                             and its grille the user's;
                             the Datsun's model and its two chrome materials; the quad's third-person
                             driving clip
  config\                    the active_camerathird control and its binding, as sections
  languages\                 the control's label, in every language
  soundbinary\               every car's engine, idle and start sounds, the ZIL-130's, the Unimog's and
                             the quad's engine-off, the gear-change clunks, the retail pedal sounds
                             silenced; every car's quieter tyre sounds
  worlds\                    the big truck's archetypes in each world's entity library, which name its
                             engine-off sound
layer\plugins\vehicle-overhaul\   the built plugin, staged here by build.ps1
```

A car is known by its archetype's `iAnimVehicleType`, one per kind of car, not by its mass or engine, so a
mod that retunes the cars keeps them known. The taxi shares the Wrangler's.

Needs FCSE 1.4.0 or later. The control sections and the archetype fragments need JackAll 1.4.0 or the
Vortex extension 0.4.0 or later, which merge them with other mods'.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\vehicle-overhaul\VehicleOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```
