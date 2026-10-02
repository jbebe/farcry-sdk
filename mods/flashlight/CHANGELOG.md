# Changelog

Notable changes to Flashlight, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Combines with other mods that add controls.** It ships only the controls category and the action
  map it adds to, rather than whole copies of both control files, so another mod's control no longer
  replaces the Flashlight's, or the other way round. Needs JackAll 1.3.0 or the Vortex extension 0.3.0.

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
