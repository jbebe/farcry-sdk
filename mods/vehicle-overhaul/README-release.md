# Vehicle Overhaul

Driving in Far Cry 2 as off-road driving: a third-person view, physics that let a car slide, lean
and bounce, an engine with real gears, and new engine sounds for every car.

## What it does

- **Third-person view.** The **Third-person view** control (V by default, rebindable in
  Options > Controls, under the vehicle controls) switches the car you drive to a chase camera and
  back. The game's leftover F2 binding does the same.
  - The camera sits behind the car and swings after its heading and height with a lag, so you see
    the body work on its suspension. The mouse orbits it; two seconds after the mouse stops, it eases
    back behind the car. It stays above the ground, though not out of rocks or buildings.
  - You see your whole body at the wheel, playing the game's own driving animations.
  - Getting out, or moving to a turret or a passenger seat, puts the first-person view back. The view
    you chose is kept for your next drive.
- **Off-road physics, on the car you drive.** AI drivers keep the game's physics.
  - No speed limiter (the game holds your car near 60 km/h), and longer gearing.
  - Weak brakes that lock the wheels into a skid after half a second, as on a car without ABS.
  - Tyre grip left to the ground's own friction, with no extra downforce and little rolling
    resistance, so a car coasts and slides.
  - Softer springs and dampers, so the body squats, dives and leans before it settles.
  - A wider steering lock, at a crawl and at speed.
- **Each car tuned as the real vehicle it depicts**: the Datsun 1200, the Land Rover Series III, the
  ZIL-130 big truck, the Unimog 406, the Jeep Wrangler (and the taxi), the Jeep Liberty, the buggy
  and the quad.
  - Each carries its weight as high and as far forward as the real one does, so a tall car leans
    and can tip in a hard turn, and none hops about on the game's odd weight balance.
  - The big trucks are heavy to turn and lean slowly.
  - The Land Rover, the ZIL-130 and the Unimog ride on solid axles, as the real ones do: the beam
    twists under the body to keep both wheels down over ruts and rocks.
- **A real engine.** The game makes up a car's revs from its speed. Here they come from the gearbox.
  - The engine idles, revs against a slipping clutch as you pull away, climbs to its own redline in
    each gear, and a rev limiter cuts in there.
  - Gears change as in Far Cry 3: never while the clutch is out, and up only if the next gear will
    pull, so a car climbing a hill holds its gear instead of hunting.
  - The ZIL-130 drives on its real five-speed gearbox, and the Jeep Liberty is an automatic.
- **New engine sounds for every car**, from recordings of real engines: an idle, a start that the
  engine fades in after as it catches, and the revs, each recording pitched and faded into the next.
  - A gear-change clunk on the Datsun, the ZIL-130 and the Unimog.
  - A recorded engine-off on the ZIL-130, the Unimog and the quad. The game's ZIL-130 stopped
    silently.
- **Quieter tyres.** Tyre sounds on every surface play 10 dB quieter, so they no longer drown out
  the engine.
- **Headlamps you can see.** Every lit headlamp, on any car, shows as a bright point from afar and
  glares a little up close in front of it. Whatever stands in the way hides it. The game lit the
  lenses of AI cars only.
- **New paints**, muted: the Datsun's, the Land Rover's and the big truck's, and one of the buggy's.
  Every car shows them, the ones placed in the world included.
- **The Datsun's chrome**: its bumpers, door handles, wheel rims, grille frame and badge, where the
  game made them black.
- **The big truck's grille**, with thirteen openings in place of six, and its **radiator fan**, which
  spins up when the engine starts and coasts down after it stops.

## Tuning

Every car starts at a tuning made for it in game, so you never need to touch it. To change one, edit
`bin\vehicle-overhaul.ini`, which the mod writes at every start with one section per car: engine
power, top speed, shift times, brakes, steering, grip, inertia, springs, travel and damping, and
switches for the whole overhaul, the real engine, the real centre of mass and the solid axles. A
shift time and downshift time of 0 make a car an automatic.

With DevTools, a developer plugin from the same project, its overlay has a **Vehicle Overhaul**
window with a slider for each, the car's speed, revs and gear, and the sideways g it tips over at.

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.4.0 or the Vortex extension 0.4.0, or later**, to install it.
- **UFCP**, recommended: with it, headlights light the grass, which the game leaves dark under any
  spot light.

## Installing

This archive is a JackAll layer: `mods\` at its root is the game data, `plugins\` is the FCSE plugin,
and both installers read that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer vehicle-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Mods that retune the cars** keep working with it: it knows each car by the kind of vehicle it is,
  not by its mass or engine. Its few changes to the big truck's data merge with theirs field by
  field.
- **Mods that replace car engine sounds, or the Datsun's or big truck's model**, change the same
  files. The installer reports it, and the mod later in the load order wins. Sound Overhaul is one:
  both replace the Datsun's idle.
- **Boats and the glider** are as the game has them.

## Planned

Shooting from the car's windows, and the game's wrecks revived as drivable vehicles.
