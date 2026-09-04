# Changelog

Notable changes to the VSS Vintorez, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-09-04

### Added
- **The VSS Vintorez replaces the single-player Dart Rifle** — the VSS mesh on the Dragunov's
  skeleton and animation set, in all five LOD tiers, with its collision shape, its muzzle socket and
  its bounding boxes refitted to the new geometry.
- **Textures**, including a worn control map that grimes the weapon as its condition drops, and the
  HUD, bazaar and weapon-select icons. They ship on the tier retail uses for a weapon, a 512² base
  beside a 1024² `_mip0`, so the four state files weigh what the Dart Rifle's own do.
- **The name**, in all eleven shipped languages: ten strings across the bazaar, the challenge list
  and the statistics, plus the stealth-equipment advert that lists the weapon in prose.
- **A ten-round magazine off the sniper ammo pool**, semi-automatic rather than the Dart Rifle's
  prepare-shot, and the Dragunov's jam and break behaviour in place of a weapon that never failed.
- **The shot sound**, first person and third, replaced as sound banks rather than event ids.
- **A reload animation**, registered under the `dragunov` animation package so the clip actually
  plays, with the 17 MOVE graph fragments that point the weapon's states at it.

