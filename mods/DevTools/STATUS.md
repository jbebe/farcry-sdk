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
- [done] Session log (-perftest): a per-frame CSV with CPU, memory and VRAM samples, for /perftest - fc2drive-run 2026-11-10: no CSV without the switch, one with it
- [done] Overlay on Home: the command catalog, pinned tabs, and windows from other plugins through devtools_api.h
- [done] Overlay, AI view, frame stats and fc2drive's screenshots through the engine's Present, EndScene and Reset, with no Direct3D hooks, so they work under dgVoodoo and DLSS injectors - fc2drive drove a run end to end under dgVoodoo #63
- [done] Command API: console lines and Lua on the game thread, a 277-command catalog - run, works
- [done] Environment tab: the clock and forced storm, rain and wind - forcing confirmed in game 2026-09-13
- [done] Diagnostics tab: frame, GPU and memory against a benchmark baseline
- [done] Lua tab, with script errors and System:Log lines caught
- [done] Spawner: vehicles and soldier squads - soldiers seen 2026-10-25
- [done] Armory: gives a gun, Ammo slider and Health % for the gun in hand - user-confirmed
- [done] Armory: the DLC's silenced shotgun, sawed-off shotgun and crossbow - user 2026-11-13: "devtools fix works"
- [done] Test range world, testrange - travel from world1 crashes the GOG build (Dunia+0x4CFD33), cause unknown
- [done] Range tab: a scenario per Combat Overhaul checklist row - seen 2026-10-25; arena start facing probably wrong (unchecked)
- [done] Range grass field, grass crest and tamarix bush arenas, three patrol routes - fc2drive-run 2026-11-03/04, not played
- [done] Range acacia grove west of the grass field - not deployed (signal the user first), unseen
- [done] Remote control over a named pipe (-devtoolsremote): state, goto, actions, shots, recordings - run, works 2026-10-30
- [done] Remote control for automated tests: absolute look, camera and stance in state, spawn and clear, entity health and components, soldiers' sight, any mod's setting, pause - run, works 2026-11-09
- [done] Remote control: one pipe per install, named after its folder, so game copies run side by side - built, not run
- [done] Remote control: `send <channel> <text>` to a plugin listening on an FCSE channel - built, not run
- [new] Remote control: entities of an archetype near the player, through the engine's sphere query - deferred, no test needs it yet
- [new] Remote control: each soldier's current brain task, to replace Combat Overhaul's -braintrace in tests
- [done] Combat log (-combatlog): hits on the player, his hits on soldiers and deaths, and every mod's lines from FCSE's combat channel, in one session file; fight_report.py moved here - fc2drive damage run, GOG 2026-11-11

## To validate

- A second copy of the game starting beside the first - unseen
- The Range tab's arena start facing - probably wrong, unchecked
- The grass field, grass crest and tamarix bush arenas - fc2drive only
- The overlay on Home and its windows through the engine's Present, without and under dgVoodoo - fc2drive saw the FPS readout and the AI view only
## Approvals

## Checks

## Release order

- after fcse-1.5.0

## Collisions

- vehicle-overhaul: fixed by VO's dispatcher hooking the function entry, stacked on DevTools' hook - the same function entry
- map-overhaul: fixed by Map Overhaul's pause-listener patterns starting 10 bytes in - the pause listener's entry, which DevTools hooks
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
- 0x001449B0: stacks - the input pass, which DevTools, Flashlight and Third Person each hook as g_inputPass; FCSE stacks same-address hooks
- 0x00422510: stacks - the engine device's EndScene, which Sky, Aiming, Vehicle and Inventory Overhaul, Third Person and DevTools hook; FCSE chains them
- sky-overhaul: stacks - both hook the engine device's EndScene, which FCSE chains
- 0x00422630: stacks - the engine device's Reset, which Aiming, Vehicle and Inventory Overhaul and DevTools hook; FCSE chains them
- 0x00345C70: stacks - DevTools' precache and UFCP's frame-limiter mid-hooks sit 0x4A1 bytes apart on both builds, neither inside the other's pattern
