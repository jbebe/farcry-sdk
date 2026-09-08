# Changelog

Notable changes to UFCP, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## Unreleased

Most of this is ported from FC2JackalFix (MIT, Joshhhuaaa and TGP482) — see the README's credits.

### Added

- Fixes: the mouse speed cap, controller vibration, the loading screen's timer resolution, and the
  engine's CPU and GPU utilisation.
- Options: viewmodel, ironsight and vehicle fields of view; mouse and controller look sensitivity;
  controller aim assist; aim, controller aim and sprint toggles; full turn rate while sprinting;
  skip intro videos; skip title screen; maximum frame rate; borderless display mode.
- `src/engine/`, for seams shared by more than one feature: which input device is in use, the single
  `RunGame` detour, and a byte patch an option can turn off again.

### Changed

- **Three options now default to changing the game**, where every option previously defaulted to
  leaving it as it shipped: skip intro videos, skip title screen, and a 60 Hz frame cap. Each is one
  row away from the stock behaviour.
- **Field of view** is now the whole feature rather than the base value alone. It covers the weapon
  and arms, the sights and vehicles, and carries the compensations that keep cutscenes, ladders, the
  hang glider, muzzle particles and the map's markers correct at a raised field of view.
