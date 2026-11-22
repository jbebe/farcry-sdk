# Changelog

Notable changes to Third Person, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Fixed
- **Works with the next Sky Overhaul.** It stores the game's depth image in half floats, which this
  mod's depth lookup turned away, leaving it without the scene's depth.

## [1.0.1] - 2026-11-17

### Changed
- **Needs FCSE 1.5.0.** Built for its plugin API 10; an older FCSE leaves this mod unloaded and says
  so in `fcse.log`.

### Fixed
- **Picking something up in third person no longer leaves you stuck mid-reach.** A gun, ammo, the
  medicine box, a diamond briefcase, a door or the bazaar computer is seen in first person while you
  use it, and the view goes back over your shoulder once you are done.
- **The crosshair over the shoulder no longer goes missing** after the machete or another weapon
  without one: drawing a gun in third person brings it back, as it does in first person.
- **The chase camera no longer snaps into the car** at every small leaf: behind the car it is only
  kept above the ground.
- **The shadow under your feet shows under dgVoodoo and DLSS injectors,** and stays when the game's
  window is resized.

## [1.0.0] - 2026-11-07

### Added
- **A Third-person view control**, on V by default and rebindable under Misc in Options > Controls,
  for first or third person on foot and at the wheel.
- **Third person on foot**, over the right shoulder, with the game's own crosshair and your shots
  landing on it. Aiming brings the camera closer and tightens the crosshair. You carry your gun low
  until you aim or fire, and the map, phone and notebook stay off your belt.
- **A chase camera at the wheel**, behind the car, swinging after it with a lag and orbited by the
  mouse, with you seen driving. It was Vehicle Overhaul's, which no longer has one.
- **A camera that keeps out of walls**: it pulls in quickly where the ground, a wall or a rock comes
  between you, and eases back out.
- **Your legs in first person**, striding as you walk and crouching with you, and a soft shadow
  under your feet.
