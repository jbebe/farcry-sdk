# Changelog

Notable changes to Aiming Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **Lean**, on two new controls in Options > Controls under Movements: Lean left on Q and Lean
  right on E. The upper body slides out and bends to that side, so the eye comes about 25 cm past a
  corner, with the view, the arms and the gun, and shots leave from where the eye now is. Hold the
  key to lean while it is held, or tap it to lean until it is tapped again. Sprinting ends a lean.
- **The sway moves the aim.** The breath and a slow wander now move the view, the gun and the shot
  together, by about 0.2° down iron sights, 0.35° through a holographic sight and 0.7° through a
  magnifying scope, and the mouse corrects it as it would a real hand. Each breath draws in, lets
  out and rests calmly at the bottom: the moment to time a shot.
- **Heart rate**, from 70 a minute at rest up to 170. It climbs as stamina runs down and jumps with
  a wound or a blast nearby. Each beat jolts the aim, and a racing heart breathes faster, sways
  wider and holds the breath for less. Down a magnifying scope a racing heart is heard faintly.
- **Stance.** Crouched and still, the sway is half what it is standing; moving widens it.
- **Hold breath** on the Dart Rifle, the M1903, the Dragunov and the AS50: hold sprint down the
  scope and the breath settles and the aim steadies, for 4 s with a calm heart and 2 s with a racing
  one, heard drawn in and let out. Those four scopes sway twice as far when the breath is not held.
- **Gun near walls.** A gun that would reach into a wall swings down out of it and does not fire
  until it is back up. The sights come back by themselves if the aim key is still held.
- **Hold breath** and **Gun near walls** switches in the Mod Configuration menu.

### Changed
- **Interact moves to F, throw grenade to G and next throwable to T** by default, to free Q and E
  for the lean. All three can be rebound. Leaving a vehicle or a mounted gun, a buddy rescue, the
  machete finish and the watch stay on E and Q, which the game never lets be rebound.
- **No random spread down the AR-16's and the MGL-140's scopes**, so a scoped shot goes where the
  reticle is, as it already did on the sniper rifles.

## [1.2.0] - 2026-10-15

### Added
- **Iron sight zoom** in the Mod Configuration menu. Switched off, the view no longer zooms in
  down a gun's iron sights; scopes still magnify. On by default, as the game has it.

## [1.1.0] - 2026-10-09

### Changed
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.
- **The reticles are PNG files** beside the plugin, in `bin\plugins\aiming-overhaul\reticles\`,
  so one can be replaced by overwriting it, and a weapon's data can name an image of another mod's.
- **Scopes are weapon data.** How each scope is drawn, its reticle, rim, lens and how far the eye
  comes to it, is now set on the weapon in the mod's data, and the mod finds the scope in the
  weapon's own mesh instead of recognising each rifle's model. A weapon mod can give its own gun a
  scope the same way.

## [1.0.0] - 2026-10-06

### Added
- **Sway down the iron sights.** The eye drifts a couple of millimetres off the gun and rises and
  falls with a slow breath, so the rear and front sights drift out of line as on a real gun. Shots
  still go where the game sends them.
- **A blurred gun down the iron sights.** The eye focuses on the front sight and the rest of the
  gun goes out of focus by how far it is from it, the rear sight most. It holds through fast turns.
  A pistol's sights are too close together to blur.
- **Scopes seen from the eyepiece.** Instead of a long tube, every scope is a black, out-of-focus
  eyepiece spanning 80% of the screen, with the zoomed view filling its opening. The view inside is
  seen through glass, bowing and fringing slightly toward the rim, and the scope trails the look a
  little and kicks back with each shot.
- **New reticles**: a hunting duplex on the Dart Rifle and the M1903, a PSO-1 with its rangefinder
  on the Dragunov, a red tactical mil-scale on the AS50, and a lit holographic chevron on the AR-16
  and the MGL-140 that burns brighter inside its strokes and glows past them.
- **The world around a scope**, at your normal field of view and slightly out of focus, as with both
  eyes open. The magnification is there at once inside the eyepiece, so the view no longer zooms as
  the scope comes up, and the game's radial blur around scopes is left out.
- **A scope shadow**: a soft dark crescent comes in from the eyepiece's rim on a fast turn.
- **Tracers** from the M249, the PKM, the Dragunov, the AS50 and the mounted machine guns, your own
  shots included: bright orange streaks at 350 m/s, smooth at any angle and visible however far
  off. One in six glances off where it lands, up into the air.
- **A switch for each** in the Mod Configuration menu.
- **Weapon data that merges.** Which guns fire tracers, how fast they fly and the scopes' iron-sight
  effect are set as weapon data, so other weapon mods merge with it field by field, and a clash on
  the same field is reported by the installer.
