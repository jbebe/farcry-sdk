# Changelog

Notable changes to Aiming Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.
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
