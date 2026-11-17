# Third Person

Purpose: The player's own body: legs and a shadow under the first-person view, and a third-person view on foot and at the wheel.
Stage: active

## Features

- [done] Legs and boots under the first-person view, a stride that follows the movement, two-bone IK - seen in scripted shots (e7c1ccd3)
- [done] A crouch with the knees out, so the lowered eye sees past them - seen in scripted shots
- [done] The whole body seen from a camera away from the eye, such as DevTools' free camera - seen in scripted shots
- [done] Feet tucked up in the air - user 2026-11-17 checklist: works
- [done] A soft round shadow under the feet in first person - user: "it looks good" (4134a2e5)
- [done] The shadow read from the engine's own view depth and drawn at the engine device's sky pass, with no Direct3D hooks, so it works under dgVoodoo and DLSS injectors and stays after a resize - matches the old build (fc2drive); user 2026-11-14: "they work alright" #63
- [done] A Third-person view control on V, rebindable under Misc, on foot and at the wheel, kept for the session (db5067a)
- [done] Third person on foot over the right shoulder: the game's own crosshair, shots landing on it, no iron sights (73a5efe9); the crosshair back after the machete, user 2026-11-17: "yes"
- [done] The aim key brings the camera closer over the shoulder and the crosshair to its tightest - user 2026-11-17 checklist: works
- [done] The gun carried low on the move, raised to aim, fire and for 1 s after a shot - user 2026-11-17 checklist: works
- [done] A pickup, the medicine box, a briefcase, a door or the bazaar computer used over the shoulder plays in first person, then back to third person - Nexus bug, user 2026-11-17: "it works good"
- [done] Map, phone and notebook hidden from the belt in third person - per-frame poll "all works!"; its one-shot rewrite not seen
- [done] Chase camera behind the car at the wheel, orbited by the mouse - moved from Vehicle Overhaul (db5067a), there "works perfectly"
- [done] The driver's whole body at the wheel, the quad's steering centred - moved from Vehicle Overhaul, there "it works!"
- [done] The camera on foot pulled in where the ground, a wall or a rock comes between, eased back out (boom)
- [done] No camera collision behind the car, so small leaves stop pulling the view into it, only the terrain floor - #39, user 2026-11-12: "it works!"
- [scrapped] A recognisable full-body or sun-cast shadow - user: "something blurry, something unrecognizable and easy to render"
- [scrapped] Right click swooshing into first person - the user stopped it midway; not re-added unless asked
- [scrapped] A crouch-walk animation - the game has none; user: "keep the crouch as is. I dont care"

## To validate

- From third person: an ammo pile, a diamond briefcase, an ordinary door, the bazaar computer, a gun swap that holsters first - not tried; guns, the medicine box and safe-house doors were

## Approvals

- played: db5067a 2026-11-07 - the deployed build with camera collision, then "let's prepare the mod for release" (commit: played by the user)
- page text: ccf0eed 2026-11-08 - implied by the third-person-1.0.0 release

## Checks

- simplify: 15a373b6 2026-11-17 - since third-person-1.0.0; 38d37828 covered up to it, then the interaction hold-back: two small cuts

## Release order

- after fcse-1.5.0

## Collisions

- vehicle-overhaul: fixed by db5067a - both hook SetDriver (patterns wildcard its first 6 bytes); VO 1.0.1 and earlier have their own view on V
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
- 0x001449B0: stacks - the input pass, which DevTools, Flashlight and Third Person each hook as g_inputPass; FCSE stacks same-address hooks
- 0x00422510: stacks - the engine device's EndScene, which Sky, Aiming, Vehicle and Inventory Overhaul, Third Person and DevTools hook; FCSE chains them
