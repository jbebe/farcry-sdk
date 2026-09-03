# Changelog

Notable changes to the VSS Vintorez, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-09-03

### Added
- **The VSS Vintorez replaces the single-player Dart Rifle** — the VSS mesh on the Dragunov's
  skeleton and animation set, in all five LOD tiers, with its collision shape, its muzzle socket and
  its bounding boxes refitted to the new geometry.
- **Textures**, including a worn control map that grimes the weapon as its condition drops, and the
  HUD, bazaar and weapon-select icons.
- **The name**, in English: ten strings across the bazaar, the challenge list and the statistics.
- **A ten-round magazine off the sniper ammo pool**, semi-automatic rather than the Dart Rifle's
  prepare-shot, and the Dragunov's jam and break behaviour in place of a weapon that never failed.
- **The shot sound**, first person and third, replaced as sound banks rather than event ids.
- **A reload animation**, registered under the `dragunov` animation package so the clip actually
  plays, with the 17 MOVE graph fragments that point the weapon's states at it.

