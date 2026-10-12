# Changelog

Notable changes to UFCP, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
