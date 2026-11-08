# Sky Overhaul

Purpose: A new sky, clouds, sun and light for Far Cry 2, drawn from the sun's position, plus the night, grade, foliage, rock, shadow and haze passes the user widened it to.
Stage: maintenance

## Features

- [done] The sky from the sun: blue noon, sunsets, twilight, stars all night; land fog takes its horizon hue - day cycle "like clockwork"
- [done] Volumetric clouds, a cirrus sheet and aircraft trails, drifting with the game's clock
- [done] Storms: thicker clouds, cirrus over the whole sky, grey light, the clear fog kept - confirmed in game 2026-09-13
- [done] Clear days not blown out: the sky at half brightness and sunlit clouds at a quarter once the sun is up
- [done] The game's clouds stay until ours draw, so a Direct3D injector never leaves an empty sky
- [done] Sun glare and afterimage, covered by our clouds - cloud cover confirmed in game 2026-09-13
- [done] Light shafts that stop where our clouds stand - confirmed in game 2026-09-13
- [done] Cloud shadows on the ground - needs depth pass quality high
- [done] Night: a smaller unfogged moon, moonlit clouds, blue-grey preset nights with moon shadows - user: "looks really nice"
- [done] Night eye pass: the world's colour drains to rod blue-grey in the dark, lights keep theirs
- [new] Night eye pass off indoors - known issue; two raycast attempts crashed and were reverted, needs another route
- [done] Colour grade override: neutral, no yellow tint - the user's tuned values are the defaults
- [done] Ambient occlusion, off by default - fades into fog
- [done] Grass lit by the sun: dark roots, sheen, light through the blades - user: "looks nice"; bent and shadowless grass not covered
- [done] Tree leaves lit one by one - user: "perfect"
- [done] Rocks and cliffs shaded as faceted stone with a CC0 detail texture, smooth with distance - user: "look just as we wanted"
- [done] Sun shadows reach farther and turn in half-degree steps so building shadows hold still - user-picked ranges 51/100/207 m
- [done] Dusk fog: the land's fog darkens as the sun sets - the user has not confirmed the one-frame flash fix
- [done] Daytime ambient lift in the clear preset, up to 1.7x at noon - widened at the user's call after a tester's report
- [done] Film grain
- [done] Heat haze: drifting waves, far land smudged, hilltops calm - user-tuned defaults, on by default; user: "is great"
- [done] Mirages on bare level ground far off - user: "now it works perfectly"; scope zoom fix "perfect"
- [done] Land to the horizon where the drawn world ends short - user: "its good like this" (2026-10-16)
- [new] The land chunks the engine skips by view position - anti-portals suspected; next a DisableAntiPortals test layer
- [done] Flatten, off by default - user: "it looks perfect. beautiful."; shots past ~250 m land off, no fade when zoomed
- [done] Each part switched in the Mod Configuration Menu, values in bin\sky-overhaul.ini and a DevTools window
- [scrapped] Tonemap or highlight roll-off - user: "useless" (2026-09-15); never propose again
- [scrapped] Per-moment sky presets and moment tabs - user found them overwhelming; the sky follows the sun, sliders only on request
- [scrapped] Sun shadow softening - did not reach the distant shadows, reverted; never propose again
- [scrapped] Heat haze far blur - user: "not important"; never propose again
- [scrapped] Re-faceting the rock meshes - abandoned; the user wanted the retail mesh lit better
- [scrapped] Dark adaptation, night dazzle, a black-level curve, acuity blur - declined; FC2 stays a casual FPS
- [scrapped] Weather controls - developer settings, they live in DevTools' Environment tab
- [scrapped] Sharpening, a longer shadow-cascade reach, FC3/FC4 RE for it - never propose

## Approvals

- played: e28a5a3 2026-10-16 - implied by the sky-overhaul-1.5.0 release
- page text: e28a5a3 2026-10-16 - implied by the sky-overhaul-1.5.0 release; changed since in e6cb22c (DirectX 9 note)

## Checks

## Release order

## Collisions

- aiming-overhaul: fixed by AO's stencil bit 0x20, gun blur before bloom and Reset via the device vtable - Direct3D hooks, stencil 0x40
- vehicle-overhaul: fixed by VO's lamp glow hooking the device vtable's Reset - Sky's engine teardown pattern fails once hooked twice
- ufcp: fixed by design: Sky never swaps the grass spot-light shaders and that pass writes no Z - grass shaders
