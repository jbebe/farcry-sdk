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
- [done] Map icons sit still on the paper instead of shifting by less than a pixel (#40) - unreleased; user: "they look as if they are glued to the map" (2026-11-12)
- [done] Grass lit under headlights and flashlights - user: "now its perfect" (2026-10-12); 1.3.1's shadow-casting lights fix unseen
- [done] Field of view 65-140 for the view on foot, cutscenes and ladders
- [done] Weapon field of view, the same as the view by default or an angle of its own
- [done] Maximum frame rate through the engine's limiter, 60 Hz by default
- [done] Display mode: borderless
- [done] Ships as a JackAll layer, installed through Vortex or JackAll - unreleased
- [scrapped] Processor affinity option - removed in 1.3.0; the game's processors are chosen for it
- [scrapped] Extra job workers - the engine's jobs crash when two run at once (1.3.0)
- [scrapped] Skip intro and skip title screen - moved to DevTools before UFCP's first release

## To validate

- The Jackal tapes advance in the southern map - unchecked
- The menu cursor follows a slow mouse - unchecked
- Monocular look speed matching the scopes - unchecked

## Approvals

- played: 290c8ed 2026-10-30 - implied by the ufcp-1.3.1 release
- page text: e893623 2026-11-17

## Checks

- simplify: 003190f 2026-11-15 - since ufcp-1.3.1

## Release order

- after fcse-1.5.0

## Collisions

- sky-overhaul: fixed by design: Sky never swaps the grass spot-light shaders and that pass writes no Z - grass shaders
- 0x00345C70: stacks - UFCP's frame-limiter and DevTools' precache mid-hooks sit 0x4A1 bytes apart on both builds, neither inside the other's pattern
