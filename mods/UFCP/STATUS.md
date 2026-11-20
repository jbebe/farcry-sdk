# UFCP

Purpose: Fix the bugs Ubisoft never patched, in the running game or as data fragments, plus a few player preferences; no tweaks and no developer tools.
Stage: active

## Features

- [done] Jackal tapes advance in the southern map - user 2026-11-17: works; pattern missed with Legacy Plugin loaded first (unexplained)
- [done] Bonus content back: the seven predecessor missions and the Primitive and Homemade machetes - same Legacy Plugin miss
- [done] Exit crash: Exit Game closes instead of faulting - verified from the menu
- [done] Mouse speed cap: a fast flick turns as far as a slow one
- [done] Menu cursor follows a slow mouse - unreleased; user 2026-11-17: works
- [done] Monocular turns with the mouse like a scope at its zoom, slowed by its field of view as down a gun's sights - unreleased; user 2026-11-17: works; the gamepad keeps the retail 0.1
- [done] Loading screens run at their 30 FPS (high-precision timer)
- [done] CPU and GPU use: no idle spinning, the GPU a frame ahead, no busy-wait frame cap; the engine's single job worker kept
- [done] Many-core startup crash on 32+ logical processors
- [done] Hybrid CPUs run on the performance cores where they have 8+ threads
- [done] Map icons sit still on the paper instead of shifting by less than a pixel (#40) - unreleased; user: "they look as if they are glued to the map" (2026-11-12)
- [done] Grass lit under headlights and flashlights - user: "now its perfect" (2026-10-12); 1.3.1's shadow-casting lights fix unseen; swapped at the engine's own shader creation with no Direct3D hooks, so it works under dgVoodoo and DLSS injectors - unreleased; user 2026-11-18 under dgVoodoo with DLSS 5: "It works" (#63)
- [done] Field of view 65-140 for the view on foot, cutscenes and ladders
- [done] Weapon field of view, the same as the view by default or an angle of its own
- [done] Maximum frame rate through the engine's limiter, 60 Hz by default
- [done] Display mode: borderless
- [done] Weapon switch during a bolt or pump gun's cycle after a shot: the two Preparing_Weapon states added to weapons.gosm's AllowWeaponSwitch, a fragment in a layer again, merged by JackAll 1.9.0 - fc2drive (tests/weapon_switch.py): the pistol up 0.6 s after an M1903 shot against 2.0 s as shipped, the first ~0.35 s of the shot still drops a press; the rifle fires when drawn back #81
- [scrapped] Ships as a JackAll layer with the monocular's gamepad look speed 0.061 - dropped by the user 2026-11-17; a plain DLL
- [scrapped] Processor affinity option - removed in 1.3.0; the game's processors are chosen for it
- [scrapped] Extra job workers - the engine's jobs crash when two run at once (1.3.0)
- [scrapped] Skip intro and skip title screen - moved to DevTools before UFCP's first release

## To validate

- Fire the M1903 or the Ithaca and press 2 or 3 at once: the other gun comes up as soon as the shot's kick is over, before the bolt or pump is worked; back on the rifle, it works its bolt and fires - fc2drive only

## Approvals

- played: 3056ed3 2026-11-18
- page text: 3dcbf56 2026-11-17

## Checks

- simplify: 3056ed3 2026-11-18 - since ufcp-1.3.1

## Release order

- after fcse-1.5.0
- after jackall-1.9.0
- after vortex-0.8.0

## Collisions

- sky-overhaul: fixed by design: Sky never swaps the grass spot-light shaders and that pass writes no Z - grass shaders
- 0x00345C70: stacks - UFCP's frame-limiter and DevTools' precache mid-hooks sit 0x4A1 bytes apart on both builds, neither inside the other's pattern
