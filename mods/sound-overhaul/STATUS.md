# Sound Overhaul

Purpose: Far Cry 2 sounding like a serious shooter: the authored reverb back, realistic gunshots, echoes, muffling and falloff.
Stage: maintenance

## Features

- [done] The game's authored reverb back through the bundled DSOAL - user: hangars "echo nicely"
- [done] The reverb switching as the player moves, PlaySoundReverb given a body
- [done] Every building's reverb by its type and size, on presets retail never used - user: "super realistic"
- [done] A short hard slap off a small room's walls
- [done] Walls muffle by material; open doors and windows let the outside in by their size - heard; echo-in-rooms fix unchecked in game
- [done] The player's own shots and reloads take the room, 2D reverb send +1,000 mB - user: "now its perfect"
- [done] New first- and third-person shots for every single-player weapon, from Dark Signal - every gun replaced 2026-09-29
- [done] An echo after every unsuppressed shot, at a level per gun graded by the real weapon's muzzle blast - #9 - user: "all is good, sound-wise"
- [done] Full-auto plays a shot on every round
- [done] NPC shots echo, turn into a crack from 40 m, and only a burst's last echo rings out
- [done] New grenade and rocket blasts, close, medium and far
- [done] A Combat music setting in the Mod Configuration Menu
- [done] Voices fall off like speech, gone by 50 m - user: "good now" (206f6d5)
- [done] No distant dogs and jackals in the ambience
- [done] The player's own pickups never muffled - heard working
- [done] A DevTools window muting single shots, full-auto fire or echoes
- [done] Enumeration fix so an older or Wine-derived dsound.dll opens a device
- [scrapped] Speed-of-sound delay - prototype rejected 2026-09-29; never re-propose
- [scrapped] Engine-made gunshot echo through EAX - user: "all of them is awful"; never re-propose
- [scrapped] Ricochets, wizz, air absorption, voice cap, ducking, weapon extras, tails, foley - the user cut the sound list 2026-10-01
- [scrapped] Datsun engine sound overhaul - abandoned 2026-09-30 ("abandon the ship!"); the quieter idle kept then (3283244) removed 2026-11-13

## To validate

- No echo in rooms after the muffling fix - unchecked
- The Datsun idles as in the game as shipped, with Sound Overhaul installed

## Approvals

- played: b655d21b 2026-11-13 - "all is good, sound-wise"
- page text: 3d0dc81 2026-10-18 - implied by the sound-overhaul-1.0.3 release

## Checks

## Release order

- after fcse-1.5.0

## Collisions

- aiming-overhaul: merged by JackAll, no shared field, and the shot hooks stack, fixed in 1.0.2 - both edit the guns' entity-library archetypes, Aiming Overhaul leaving the echo field as shipped, and both hook the shot at 0x00115680
- difficulty-overhaul: merged by JackAll, no shared field - both edit the guns' entity-library archetypes
- vss-vintorez: fixed by loading the VSS after Sound Overhaul - both ship the Dart Rifle's shot banks 004bf5e9 and 004bf5eb
