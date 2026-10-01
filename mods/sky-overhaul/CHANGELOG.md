# Changelog

Notable changes to Sky Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **Film grain**, off by default: turn it on with **Grain** in the Mod Configuration menu. Its
  strength, size and colour are under **Post FX** in the DevTools window, the tab that was **Grade**.

## [1.2.0] - 2026-09-28

Upgrading: delete `bin\sky-overhaul.ini` once. A file kept from 1.1.0 holds the old values, so the
new glare and grade below would not take effect.

### Added
- **Grass lit by the sun**, with dark roots, a sheen across the blades and light glowing through
  them, in place of the engine's flat grass lighting.
- **Tree leaves lit one by one**: a crown shades its own far side from the sun, each leaf tilts its
  own way, and leaves glint in the sun and glow with it behind them.
- **Rocks and cliffs** shaded as the faceted stone their meshes are, with a fine detail texture from
  Poly Haven's CC0 rock scan and a tight glint off their faces.
- **Sun shadows** that follow a low sun further toward the horizon and reach further from the
  player, turned with the sun in half-degree steps so thin shadows on buildings hold still.
- **Dusk fog**: the land's fog darkens as the sun sets.

### Changed
- **Clear days are no longer blown out.** Once the sun is well up, the sky is drawn at half its
  brightness and sunlit clouds at a quarter, so the sky reads blue instead of clipped cyan and the
  clouds keep their form. Dawn, dusk and storms are unchanged.
- **Shade is lighter on clear days**: up to 1.7 times the light at noon, so sunlit ground is about
  4.5 times as bright as shade instead of 7. Jungle, cloudy and storm weather and the night are
  unchanged.
- **The sun's glare stays around the sun.** The whole view washes out, loses contrast and drains of
  colour only when looking straight at it; with the sun off to one side, the rest of the view keeps
  its tones.
- **Retuned defaults**: a stronger contrast and lighter shadows in the grade, stronger ambient
  occlusion, softer cloud shadows.
- **A smaller moon.**

### Fixed
- Distant land is no longer darkened all day, only at dusk.

## [1.1.0] - 2026-09-15

### Added
- **Ambient occlusion**, darkening the corners and creases the light doesn't reach, from a depth of
  the world drawn a second time at half resolution, without its grass, leaves or the player's own
  weapon, blurred and multiplied into the world at the sky pass alongside the cloud shadows. Switched
  on its own in the Mod Configuration menu.

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
