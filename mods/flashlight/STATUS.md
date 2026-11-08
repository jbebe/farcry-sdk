# Flashlight

Purpose: A flashlight on its own rebindable control, with a click and a HUD icon, that guards can see at night.
Stage: maintenance

## Features

- [done] Flashlight control on L, rebindable, named in every language the game ships
- [done] A real spot light at the head: the headlights' warm white, 25 m, casting shadows
- [done] Switch click, from an unused game sound, with the light coming up just after it
- [done] HUD icon above the diamond counter, on or off, fading with the HUD
- [done] Cone (small, medium, large) and Shadows settings in the Mod Configuration Menu
- [done] Guards see it: a lit light counts as just fired - always on; not hooked beside Difficulty Overhaul, fix not chosen
- [done] A guard inside the beam sees all around him - worked from behind in game 2026-10-02; full vs half cone angle unverified
- [done] In the dark, guards looking your way see it from up to 60 m - user lowered it from 100 m; unplayed
- [done] Off in vehicles: getting in switches it off with its click
- [done] Works in safe houses and briefing rooms
- [done] No crash when part of the game can't be hooked; what is left out is logged in fcse.log
- [done] Combines with other mods' controls, HUD parts and HUD dependency lists
- [scrapped] Light as a noise, a plain 15 m radius - user chose the beam rule

## Approvals

- played: 49624dc 2026-10-29 - implied by the flashlight-1.2.1 release
- page text: 49624dc 2026-10-29 - implied by the flashlight-1.2.1 release

## Checks

## Release order

## Collisions

- vehicle-overhaul: deploy apart - user 2026-10-08: "flashlight is not needed, dont deploy it"; the hooks don't clash
- map-overhaul: fixed by 137373d1 - its pause-listener pattern starts 10 bytes in, past the instructions Flashlight's hook overwrites
- inventory-overhaul: fixed by 98535d3c - the notebook's bare quads took no flashlight light; page.py cuts them into cells
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
