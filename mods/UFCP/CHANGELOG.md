# Changelog

Notable changes to UFCP, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Field of view** now sets the weapon and arms too, one slider for both. They used to stay at the
  game's own 75 degrees whatever the setting.
- **Field of view** follows the game's own widescreen field of view setting again (`WidescreenFOV`
  in `GamerProfile.xml`, on by default). With it off, the view was widened as if it were on.

## [1.1.1] - 2026-10-15

### Fixed
- **Startup crash on many-core PCs.** On a PC with 32 or more logical processors, such as a 16-core
  CPU with hyperthreading, the game closed a few seconds into the splash screen. The game's own
  hardware detection counted the processors in a loop that never ended on such a machine; it now
  stops after the 32nd.

## [1.1.0] - 2026-10-15

### Changed
- **Field of view** now applies in cutscene-style first-person animations (healing, repairs, doors,
  getting in and out of vehicles) and on ladders, which used to pull it back to a narrower framing.
  The hang glider is no longer narrowed either and flies at its own angle, as the game shipped.
  At an arms dealer's computer the view is the game's own again, zoom onto the screen included, and
  eases back to the setting as the player gets up.

## [1.0.0] - 2026-10-12

First release. UFCP fixes bugs Ubisoft never patched, in the running game rather than in the files
on disk. It is an FCSE plugin and needs FCSE 1.4.0 or later.

### Fixed
- **Jackal tapes** advance through the set again. In the southern map the same recording, usually
  #09. Stealing Boots, played on every pickup.
- **Predecessor tapes**: the seven Intel Bonus predecessor missions are back. They ship in the game
  but sat behind an ownership check that can no longer succeed.
- **Machetes**: the Primitive and Homemade machetes are back, behind the same kind of check. Pick one
  in Options → Game → Machete Type.
- **Exit crash**: quitting through Exit Game closes the game instead of crashing it.
- **Mouse speed cap**: a fast flick no longer turns the view less far than a slow one.
- **Loading screens** run at the 30 FPS they are paced for, not far below it.
- **Grass under spot lights**: headlights and flashlights light the grass they shine on.
- **CPU and GPU use**: the engine uses every processor it has, stops spinning idle threads and
  burning a whole core on its own frame cap, and lets the GPU work a frame ahead.

### Added
- **Field of view**, 65 to 140 degrees, for the view on foot. The weapon and arms, the sights,
  scopes and vehicles keep their own, and cutscenes, ladders and the hang glider keep their framing.
- **Maximum frame rate**, through the engine's own limiter: 60 Hz by default, unlocked, the screen's
  refresh rate or 30 to 240 Hz. A change applies without a restart.
- **Display mode**: borderless, from the next launch.
- **Processor affinity**: all cores, physical cores only, 4 cores or 1 core. Fewer processors is the
  old workaround for NPCs bouncing on machines with many cores, at a cost in performance.

Much of the input, field-of-view, frame-rate and utilisation work is ported from FC2JackalFix
(https://github.com/TGP482/FC2JackalFix), MIT, by Joshhhuaaa and TGP482.
