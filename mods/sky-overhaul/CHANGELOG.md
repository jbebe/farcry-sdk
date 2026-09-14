# Changelog

Notable changes to Sky Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-09-14

### Added
- **The sky**, worked out from the sun's position in place of the engine's painted dome: blue at
  noon, orange and purple sunsets, a lingering twilight and stars that stay out all night. The
  land's distant fog takes the sky's horizon colour.
- **Volumetric clouds** with silver linings and dark bases, a high cirrus sheet above them, and
  aircraft trails.
- **A sky that follows the game's clock.** The clouds drift with it, so loading a save shows the sky
  that was left, and time spent or slept through moves it on. Every midnight rolls a new set of one
  to three aircraft trails.
- **Storms** thicken the clouds, close the cirrus over the whole sky and grey the light, while the
  land keeps the clear weather's fog.
- **The sun**: light shafts that stop where the clouds stand, a glare over the view when looking
  toward the sun that the clouds cover, and an afterimage after staring at it.
- **Cloud shadows** on the ground, which need depth pass quality high or above.
- **The night**: a smaller moon, unhidden by fog and lighting the clouds, darker blue-grey nights
  with moonlight that throws shadows once the moon is up, and the world's colour draining where it
  is dark while fires and lamps keep theirs.
- **A neutral colour grade** in place of the game's yellow cast, tuned by saturation, contrast,
  brightness, warmth and tint.
- **Every part switchable** in the Mod Configuration menu, every value in `bin\sky-overhaul.ini`, and
  a window of sliders in the DevTools overlay when DevTools is installed.

### Known issues
- The night's colour drain also applies indoors.
