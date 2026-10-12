# Changelog

Notable changes to UFCP, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## Unreleased

Most of this is ported from FC2JackalFix (MIT, Joshhhuaaa and TGP482) — see the README's credits.

### Added

- Fixes: the mouse speed cap, the loading screen's timer resolution, and the engine's CPU and GPU
  utilisation.
- Fix: grass lit under spot lights. Headlights and flashlights now light the grass they shine on.
- Options: mouse and controller look sensitivity; maximum frame rate; borderless display mode.
- `src/engine/`, for seams shared by more than one feature: the single `RunGame` detour, and whether
  an engine pointer is still readable.

### Changed

- **The frame cap now defaults to 60 Hz**, where every option previously defaulted to leaving the
  game as it shipped. It is one row away from the stock behaviour.
- **Field of view** is now the whole feature rather than the base value alone. One slider sets the
  view on foot, and carries the compensations that keep the weapon and arms, cutscenes, ladders, the
  hang glider, muzzle particles and the map's markers correct at a raised field of view.
