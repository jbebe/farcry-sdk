# UFCP

Purpose: Fix the bugs Ubisoft never patched, in the running game or as data fragments, plus a few player preferences; no tweaks and no developer tools.
Stage: active

## Features

- [done] Jackal tapes advance in the southern map - unchecked in game; pattern missed with Legacy Plugin loaded first (unexplained)
- [done] Bonus content back: the seven predecessor missions and the Primitive and Homemade machetes - same Legacy Plugin miss
- [done] Exit crash: Exit Game closes instead of faulting - verified from the menu
- [done] Mouse speed cap: a fast flick turns as far as a slow one
- [done] Menu cursor follows a slow mouse - unreleased, unchecked in game
- [done] Monocular look speed matches the scopes, a data fix in both worlds - unreleased, unchecked in game
- [done] Loading screens run at their 30 FPS (high-precision timer)
- [done] CPU and GPU use: no idle spinning, the GPU a frame ahead, no busy-wait frame cap; the engine's single job worker kept
- [done] Many-core startup crash on 32+ logical processors
- [done] Hybrid CPUs run on the performance cores where they have 8+ threads
- [done] Grass lit under headlights and flashlights - user: "now its perfect" (2026-10-12); 1.3.1's shadow-casting lights fix unseen
- [done] Field of view 65-140 for the view on foot, cutscenes and ladders
- [done] Weapon field of view, the same as the view by default or an angle of its own
- [done] Maximum frame rate through the engine's limiter, 60 Hz by default
- [done] Display mode: borderless
- [done] Ships as a JackAll layer, installed through Vortex or JackAll - unreleased
- [scrapped] Processor affinity option - removed in 1.3.0; the game's processors are chosen for it
- [scrapped] Extra job workers - the engine's jobs crash when two run at once (1.3.0)
- [scrapped] Skip intro and skip title screen - moved to DevTools before UFCP's first release

## Approvals

- played: 290c8ed 2026-10-30 - implied by the ufcp-1.3.1 release
- page text: 290c8ed 2026-10-30 - implied by the ufcp-1.3.1 release; changed since in 0edd19a (layer install note)

## Checks

## Release order

- after fcse-1.5.0

## Collisions

- sky-overhaul: fixed by design: Sky never swaps the grass spot-light shaders and that pass writes no Z - grass shaders
