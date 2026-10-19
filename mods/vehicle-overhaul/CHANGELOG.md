# Changelog

Notable changes to Vehicle Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-10-19

### Added
- **A third-person view.** A new Third-person view control (V by default, rebindable under the
  vehicle controls) puts a chase camera behind the car you drive. It swings after the car's heading
  and height with a lag, so you see the body work on its suspension, and the mouse orbits it. You
  see your whole body at the wheel, with the game's own driving animations. Getting out, or moving
  to a turret or a passenger seat, puts the first-person view back; the view you chose is kept for
  the next drive.
- **Off-road physics on the car you drive.** No speed limiter, longer gearing, weak brakes that lock
  the wheels into a skid, the ground's own grip, softer springs and dampers, and a wider steering
  lock. AI drivers keep the game's physics.
- **Each car tuned as the real vehicle it depicts**: the Datsun 1200, the Land Rover Series III, the
  ZIL-130, the Unimog 406, the Jeep Wrangler, the Jeep Liberty, the buggy and the quad. Each carries
  its weight as high and as far forward as the real one, and the big trucks turn and lean slowly.
  The Land Rover, the ZIL-130 and the Unimog ride on solid axles that twist to keep both wheels down.
- **A real engine.** The revs come from the gearbox: the engine idles, slips its clutch pulling away,
  climbs to its own redline in each gear, and a rev limiter cuts in there. Gears change as in Far
  Cry 3, never while the clutch is out, and a car climbing a hill holds its gear. The ZIL-130 has its
  real five-speed gearbox, and the Jeep Liberty is an automatic.
- **New engine sounds for every car**, from recordings of real engines: an idle, a start and the
  revs, faded and pitched to the engine. A gear-change clunk on the Datsun, the ZIL-130 and the
  Unimog, and a recorded engine-off on the ZIL-130, the Unimog and the quad.
- **Quieter tyres**, 10 dB down, so they no longer drown out the engine.
- **Headlamps you can see.** Every lit headlamp shows as a bright point from afar and glares a little
  up close, on any car. The game lit the lenses of AI cars only.
- **New, muted paints** for the Datsun, the Land Rover, the big truck and the buggy.
- **The Datsun's chrome**: its bumpers, door handles, rims, grille frame and badge.
- **The big truck's grille** with thirteen new openings, and its **radiator fan** spinning while the
  engine runs.
- **A tuning file**, `bin\vehicle-overhaul.ini`, with every value for each car, and with DevTools
  a Vehicle Overhaul window of sliders for them.
