# DevTools

Purpose: The developer side of Far Cry 2 for modders and reverse engineers - console, cheats, cameras, overlay, test range, remote control - kept out of what players install.
Stage: active

## Features

- [done] Savegame launch: -load <name>.sav boots straight into the save - run, works
- [done] Skip intro movies and the title screen - moved from UFCP
- [done] Multiple instances: a second copy starts alongside the first - unseen
- [done] Developer console: the ~57 developer-only commands listed and run
- [done] Invincibility that holds, the vehicle the player is in included
- [done] Infinite ammo with syringes still spent
- [done] Unlock all weapons across every act
- [done] Diamonds as a target that never reaches the save
- [done] Noclip and Freecam keys
- [done] Pause on P: the game clock stopped and the free camera through the frozen world
- [done] FPS counter
- [done] AI view: what each soldier sees of the player, drawn in the world - toggle works in game (2026-10-26)
- [done] Skip system detection
- [done] Fast loading, on by default
- [done] Session log: a per-frame CSV with CPU, memory and VRAM samples, for /perftest - /perftest tested
- [done] Overlay on Home: the command catalog, pinned tabs, and windows from other plugins through devtools_api.h
- [done] Command API: console lines and Lua on the game thread, a 277-command catalog - run, works
- [done] Environment tab: the clock and forced storm, rain and wind - forcing confirmed in game 2026-09-13
- [done] Diagnostics tab: frame, GPU and memory against a benchmark baseline
- [done] Lua tab, with script errors and System:Log lines caught
- [done] Spawner: vehicles and soldier squads - soldiers seen 2026-10-25
- [done] Armory: gives a gun, Ammo slider and Health % for the gun in hand - user-confirmed
- [done] Test range world, testrange - travel from world1 crashes the GOG build (Dunia+0x4CFD33), cause unknown
- [done] Range tab: a scenario per Difficulty Overhaul checklist row - seen 2026-10-25; arena start facing probably wrong (unchecked)
- [done] Range grass field, grass crest and tamarix bush arenas, three patrol routes - fc2drive-run 2026-11-03/04, not played
- [done] Range acacia grove west of the grass field - not deployed (signal the user first), unseen
- [done] Remote control over a named pipe (-devtoolsremote): state, goto, actions, shots, recordings - run, works 2026-10-30

## Approvals

## Checks

## Release order

## Collisions

- vehicle-overhaul: fixed by VO's dispatcher hooking the function entry, stacked on DevTools' hook - the same function entry
- map-overhaul: fixed by Map Overhaul's pause-listener patterns starting 10 bytes in - the pause listener's entry, which DevTools hooks
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
