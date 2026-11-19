# Sky Overhaul

Purpose: A new sky, clouds, sun and light for Far Cry 2, drawn from the sun's position, plus the night, grade, foliage, shadow and haze passes the user widened it to.
Stage: maintenance

## Features

- [done] The sky from the sun: blue noon, sunsets, twilight, stars all night; land fog takes its horizon hue - day cycle "like clockwork"
- [done] Volumetric clouds, a cirrus sheet and aircraft trails, drifting with the game's clock
- [done] Storms: thicker clouds, cirrus over the whole sky, grey light, the clear fog kept - confirmed in game 2026-09-13
- [done] Clear days not blown out: the sky at half brightness and sunlit clouds at a quarter once the sun is up
- [done] The game's clouds stay until ours draw, so a Direct3D injector never leaves an empty sky
- [done] Drawn through the engine with no Direct3D hooks, so it works under dgVoodoo, ReShade and a DLSS 5 injector - user: "even with dlss, the sky is right" #63
- [done] Sun glare and afterimage, covered by our clouds - cloud cover confirmed in game 2026-09-13
- [done] Light shafts that stop where our clouds stand - confirmed in game 2026-09-13
- [done] Cloud shadows on the ground - needs depth pass quality high
- [done] Night: a smaller unfogged moon, moonlit clouds, blue-grey preset nights with moon shadows - user: "looks really nice"
- [done] Night eye pass: the world's colour drains to rod blue-grey in the dark, lights keep theirs
- [done] No rim light on characters and plants in any preset, at any hour - user: "the fix for the rimlight works"
- [postponed] Night eye pass off indoors - known issue; two raycast attempts crashed and were reverted, needs another route
- [done] Colour grade override: neutral, no yellow tint - the user's tuned values are the defaults
- [done] Ambient occlusion, on by default since 1.6.0 - fades into fog
- [done] Grass lit by the sun: dark roots, sheen, light through the blades - user: "looks nice"; bent and shadowless grass not covered
- [done] Tree leaves lit one by one - user: "perfect"
- [done] Sun shadows reach farther and turn in half-degree steps so building shadows hold still - user-picked ranges 51/100/207 m
- [done] Dusk fog: the land's fog darkens as the sun sets - the user has not confirmed the one-frame flash fix
- [done] Daytime ambient lift in the clear preset, up to 1.7x at noon - widened at the user's call after a tester's report
- [done] Film grain
- [done] Heat haze: drifting waves, far land smudged, hilltops calm - user-tuned defaults, on by default; user: "is great"
- [done] Mirages on bare level ground far off - user: "now it works perfectly"; scope zoom fix "perfect"
- [done] Land to the horizon where the drawn world ends short - user: "its good like this" (2026-10-16)
- [postponed] The land chunks the engine skips by view position - anti-portals suspected; next a DisableAntiPortals test layer
- [done] Flatten, on by default since 1.6.0 - user: "it looks perfect. beautiful."; shots past ~250 m land off, no fade when zoomed
- [done] Land to the horizon: its brightness per clock hour, the user's tuned values hardcoded, no sliders
- [done] Land to the horizon hides the sun behind it, its glare and light shafts with it - user: "works"
- [done] Stars and the Milky Way fainter all over: about half - 70% was not enough for the user
- [done] Each part switched in the Mod Configuration Menu, values in bin\sky-overhaul.ini and a DevTools window
- [done] A third less GPU time: the sky from a table drawn once a frame, clouds marched at half resolution, haze with two noise reads a sheet - +2.15 to +1.47 ms a frame at 1080p on an RTX 4060 Ti; the clouds' half resolution accepted by the user from pictures
- [postponed] The solid depth (Occlusion, Heat haze) in one target switch: it costs ~0.5 ms, ~0.3 of it switching targets around each draw; collect and replay the draws after the depth pass - the user left it for now (2026-11-19)
- [new] The ground lit by where you look from: sand, dirt and tarmac brighter with the sun at your back, a sheen toward it at grazing angles, slopes and the layers' own normal maps showing - the user's ask from the airport runway (2026-11-19)
- [postponed] Volumetric fog: a froxel volume lit through the sun's shadow cascades, a haze and a mist 1.5 m deep over the ground and water - user: "not good enough, base game fog is cheap and good" (2026-11-19); the code is kept at the local tag shelf/sky-overhaul-volumetric-fog
- [postponed] Distant dust: plumes blown off the ground in the wind's direction, seen from far off, not whole storms; 3D volumes rising anywhere, by day in dry weather, more with wind and in desert areas, none in jungle - postponed with the volumetric fog it was to be built with (user, 2026-11-19)
- [scrapped] Tonemap or highlight roll-off - user: "useless" (2026-09-15); never propose again
- [scrapped] Per-moment sky presets and moment tabs - user found them overwhelming; the sky follows the sun, sliders only on request
- [scrapped] Sun shadow softening - did not reach the distant shadows, reverted; never propose again
- [scrapped] Heat haze far blur - user: "not important"; never propose again
- [scrapped] Re-faceting the rock meshes - abandoned; the user wanted the retail mesh lit better
- [scrapped] Rocks and cliffs shaded as faceted stone - removed in 1.6.0, user: "makes rocks worse, too PS1-like"
- [scrapped] Dark adaptation, night dazzle, a black-level curve, acuity blur - declined; FC2 stays a casual FPS
- [scrapped] Weather controls - developer settings, they live in DevTools' Environment tab
- [scrapped] Sharpening, a longer shadow-cascade reach, FC3/FC4 RE for it - never propose

## To validate

- Play a while by day and at dusk: the sky, the clouds and the heat haze look as before, thin cloud edges a touch grainier
- Alt+Tab out of full screen and back, or change the resolution: the sky and the clouds come back

## Approvals

- played: 844c90c 2026-11-17 - the 1.6.0 play check, confirmed by the user as the approval
- page text: 844c90c 2026-11-17 - "they are good"

## Checks

- simplify: 272c81e 2026-11-17 - since sky-overhaul-1.5.0

## Release order

- after fcse-1.5.0

## Collisions

- aiming-overhaul: fixed by AO's stencil bit 0x20 and gun blur before bloom - stencil 0x40; both hook the engine's draws and EndScene, which stack, and AO resets through the engine's Reset, not Sky's teardown - scope and tracers checked together (fc2drive)
- vehicle-overhaul: stacks - both hook the engine's EndScene, which FCSE chains; VO's reset seam is the engine's own Reset, not Sky's teardown
- 0x00422510: stacks - the engine device's EndScene, which Sky, Aiming, Vehicle and Inventory Overhaul, Third Person and DevTools hook; FCSE chains them
- DevTools: stacks - both hook the engine device's EndScene, which FCSE chains
- ufcp: fixed by design: Sky never swaps the grass spot-light shaders and that pass writes no Z - grass shaders
