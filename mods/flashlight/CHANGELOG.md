# Changelog

Notable changes to Flashlight, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.2.1] - 2026-10-28

### Changed
- **Combines with other mods that add to what the HUD loads.** It ships only its two icons' entries
  in the HUD's dependency lists rather than whole copies of the lists, so another mod adding to them,
  such as a new font or texture, keeps its entries and the Flashlight's. Needs JackAll 1.7.0 or the
  Vortex extension 0.6.0.

## [1.2.0] - 2026-10-15

### Changed
- **Off in vehicles.** Getting into a vehicle switches the flashlight off with its click, and it
  stays off until you get out.
- **Works in safe interiors.** The game swaps in its own controls inside safe houses and briefing
  rooms, which left the flashlight out; it switches there too now.

### Fixed
- **No crash at startup when part of the game can't be hooked**, for example with two copies of
  `Flashlight.dll` installed. The flashlight then leaves out what it couldn't hook and logs it in
  `fcse.log`.

## [1.1.1] - 2026-10-09

### Changed
- **Combines with other mods that change the HUD.** It ships only the parts of `hud.mgb` the icon
  changes rather than whole copies of it, so another HUD mod that does the same keeps both its own
  changes and the icon. Needs JackAll 1.4.1 or the Vortex extension 0.4.0.

## [1.1.0] - 2026-10-09

### Changed
- **Combines with other mods that add controls.** It ships only the controls category and the action
  map it adds to, rather than whole copies of both control files, so another mod's control no longer
  replaces the Flashlight's, or the other way round. Needs JackAll 1.3.0 or the Vortex extension 0.3.0.
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.

## [1.0.0] - 2026-10-02

### Added
- **A flashlight on its own control**, Flashlight, on L by default and rebindable in Options >
  Controls. The control is named in every language the game ships.
- **A real light.** The engine's own spot light, the kind the vehicles' headlights use, sits at your
  head and follows where you look. It is the headlights' warm white, reaches 25 m and casts
  shadows.
- **Guards see it.** At night a guard looking your way sees a lit flashlight from up to 60 m, and a
  guard you shine it on within 25 m notices you even with his back turned. Switch it off and they
  lose the light about 3 seconds later.
- **A switch click.** The click plays at once, and the light comes up just after it.
- **A HUD icon** above the diamond counter, showing whether the light is on or off. It fades in when
  you toggle and out again with the rest of the HUD.
- **Cone and Shadows settings** in the Mod Configuration menu: a small, medium or large beam, and
  shadows on or off. Both apply to a light that is already on.
