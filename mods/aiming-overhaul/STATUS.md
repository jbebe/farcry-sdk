# Aiming Overhaul

Purpose: How the player aims and moves, from the input to the shot leaving the gun, and what they see of it; no damage, no AI.
Stage: active

## Features

- [done] Sway: a breath cycle and a slow wander move the aim; down iron sights the sights drift out of line - user: "perfect"
- [done] Steadier down the sights: share of the sway left standing and crouched, set in the window - played 2026-11-08
- [done] Heart rate 70-170: beats jolt the aim, faint beat down magnifying scopes - fade "perfect" 2026-10-26, sway effects unconfirmed
- [done] Stance: crouched and still halves the sway, moving widens it - unconfirmed in game
- [done] Hold breath with sprint down the Dart Rifle, M1903, Dragunov and AS50 scopes, heard drawn in and let out
- [done] Lean on Q/E: upper body slides 10 cm and bends 15°, hold or tap - user: "perfect" 2026-10-24
- [done] Shots leave from the leaned eye - confirmed 2026-10-29; muzzle flash and scope while leaning unchecked
- [done] Default keys moved for the lean: interact to F, throw grenade to G, next throwable to T
- [done] Gun near walls: swings down out of a wall and won't fire until back up - latest changes unplayed
- [done] Vertical inertia: the arms slide behind the look's pitch - user: "perfect"
- [done] Landing: the view drops a few cm as the knees take it - user-approved
- [done] Gun up after a sprint: carried low, slower to come up the heavier it is - user-approved
- [done] Getting going: a walk takes 0.5 s and a sprint 1.5 s to reach pace; stops stay instant - user-approved
- [done] Recoil that stays: the game's kick stays in the aim, light first two kicks - unplayed
- [done] Full auto spreads from a burst's 4th shot, up to 8 shots' worth - unplayed
- [done] Operation manual halves the iron-sight spread standing, quarters it crouched - unplayed
- [done] Shot shake sized by the round's push, fading into a roll in full auto, per weapon - user-tuned, "Let's call it done" 2026-11-05
- [done] Dirt in the eye: black blurry blobs from rounds landing in dirt within 3 m, own rounds too - played 2026-11-08
- [done] Being hit: body punch and dark edges, head cut to black then a double image - played 2026-11-08
- [done] No red marks pointing to whoever hit the player - played 2026-11-08
- [done] Aiming Overhaul window in DevTools' overlay, values kept in bin\aiming-overhaul.ini
- [done] Gun blur down the iron sights, focused on the front sight - focus fix "perfect" 2026-10-06
- [done] Scope eyepiece: black out-of-focus body over the zoomed view, kicks back per shot - "its perfect now!" 2026-10-04
- [done] Glass inside the eyepiece: lines bow, colours part, rim darkens, faint tint - not confirmed by the user
- [done] New reticles as replaceable PNGs, the holographic chevron lit - "everything works perfectly" 2026-10-09
- [done] Scope surroundings at the unzoomed field of view, no zoom as the scope comes up - "everything works perfectly" 2026-10-04
- [done] Scope shadow: a soft crescent from the rim as the look turns
- [done] Tracers on the M249, PKM, Dragunov, AS50 and mounted MGs, own shots included, 1 in 6 ricochet - "All works fine" 2026-10-06
- [done] Iron sight zoom switch: off keeps the field of view down iron sights
- [done] No random spread down the AR-16's and MGL-140's scopes
- [done] Scopes as weapon data (AimingOverhaul.Scope* keys), found in the weapon's own mesh - "it seems to be working" 2026-10-08
- [done] Weapon and player data as fragments that merge field by field with other mods
- [done] A switch per effect in the Mod Configuration Menu
- [new] Forward/back bob per shot and physical sway in full auto, no climb - user 2026-11-08: "VERY needed"; proposal pending
- [on hold] Auto lean at corners - shelved until wanted
- [scrapped] Gun weight motion (lag, swing, slowdown by weight) - dropped by the user 2026-11-02 after five tries
- [scrapped] Gradual stops when the keys are let go - user rejected
- [scrapped] Iron-sight spread cut for every gun - removed at the user's ask (1b343c97); lives on only with the manual
- [scrapped] Shorter punch for a hit on the vest - vest idea scrapped (6cb34d49)
- [scrapped] Mod sounds on being hit - the game's own hit sound is enough
- [scrapped] Near-field gather gun blur and a stronger blur - user: "the blur is awful", reverted
- [scrapped] Quiet breath sound with every breath while aiming - tried, scrapped
- [scrapped] Scope lag spring and a crosshair that swings - user: keep the retail minor wiggle
- [scrapped] Smudges on the eyepiece glass - "too extra compared to the rest of the game"
- [scrapped] Resting the gun on cover - user ruled it out
- [scrapped] Grit, chips and dust sprites in dirt in the eye - user wanted only blurry blobs
- [scrapped] Operation manual extras: burst fade, new bonus attributes, window values, zeroing challenges - user rejected

## Approvals

- played: 10aebf7 2026-10-15 - implied by the aiming-overhaul-1.2.0 release
- page text: 10aebf7 2026-10-15 - implied by the aiming-overhaul-1.2.0 release

## Checks

## Release order

- after jackall-1.4.1
- after vortex-0.4.0

## Collisions

- vss-vintorez: fixed by b7946eaf - the VSS's Dart Rifle drew the game's scope until it carried AO's scope data (the Dragunov's)
- sky-overhaul: fixed by FCSE 1.3.0's hook stacking - both hook the same Direct3D functions; the gun blur sits before bloom
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
