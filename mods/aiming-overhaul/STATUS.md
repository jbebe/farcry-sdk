# Aiming Overhaul

Purpose: How the player aims and moves, from the input to the shot leaving the gun, and what they see of it; no damage, no AI.
Stage: active

## Features

- [done] Sway: a breath cycle and a slow wander move the aim; down iron sights the sights drift out of line - user: "perfect", works 2026-11-16
- [done] Steadier down the sights: share of the sway left standing and crouched, set in the window; standing, the iron sights' wander 1.6 times the eye's drift - user 2026-11-16: standing "too rigid, too precise", wander raised, unplayed
- [done] Heart rate 70-170: beats jolt the aim, faint beat down magnifying scopes - fade "perfect" 2026-10-26, works 2026-11-16
- [done] Stance: crouched and still halves the sway; moving, crouched sways as standing still and standing twice that, the wander up to twice as fast - user 2026-11-16: moving should be worse than standing, unplayed
- [done] Hold breath with sprint down the Dart Rifle, M1903, Dragunov and AS50 scopes, heard drawn in and let out - works 2026-11-16
- [done] Lean on Q/E: upper body slides 10 cm and bends 15°, hold or tap; a load, vehicle or mounted gun ends it - user: "perfect" 2026-10-24; the end on a load, user 2026-11-16, unplayed
- [done] Shots leave from the leaned eye - confirmed 2026-10-29; muzzle flash and scope while leaning work 2026-11-16
- [done] Default keys moved for the lean: interact to F, throw grenade to G, next throwable to T, leaving a vehicle or mounted gun and healing a downed buddy to F - user 2026-11-16: "If F is enter vehicle, F should be exit vehicle", buddy 2026-11-17; unplayed
- [done] Gun near walls: swings down out of a wall and won't fire until back up - works 2026-11-16
- [done] Vertical inertia: the arms slide behind the look's pitch - user: "perfect", works 2026-11-16
- [new] Inertia by turn and weight: the sideways and vertical slides bigger the faster the look turns, slower back after a hard twitch, quicker for a light gun, never at once; the scope's lag the same - user 2026-11-16; picked 2026-11-17: twice the game's slide, back in 0.2 s (pistol) to 0.6 s (PKM), slower after a hard twitch, at the hip and on the scope only
- [done] Gun up after a sprint: carried low, slower to come up the heavier it is - user-approved, works 2026-11-16
- [done] Getting going: a walk takes 0.5 s and a sprint 1.5 s to reach pace; stops stay instant - user-approved, works 2026-11-16
- [done] Recoil that stays: the game's kick stays in the aim, light first two kicks - works 2026-11-16
- [done] Full auto spreads from a burst's 4th shot, up to 8 shots' worth - works 2026-11-16
- [done] Operation manual halves the iron-sight spread standing, quarters it crouched - works 2026-11-16
- [done] Shot shake sized by the round's push, fading into a roll in full auto, per weapon - user-tuned, "Let's call it done" 2026-11-05
- [done] Dirt in the eye: black blurry blobs from rounds landing in dirt within 3 m, own rounds too - played 2026-11-08
- [done] Dirt in the eye bigger and darker, overlapping blobs darkening each other rather than the darker one showing alone - user 2026-11-14: "the current ones are too small"; built, unplayed
- [done] Being hit: body punch and dark edges, head cut to black then a double image - played 2026-11-08; punch raised to 8 cm and 5° at the user's ask 2026-11-16, unplayed
- [new] Health shown in the picture: three bars left less colourful, two less still with grain and a slight glow, the last black and white, grainy, strong glow, each fading into the next over 1 s - user 2026-11-16; picked 2026-11-17: a third of the colour gone at 3 bars, two thirds with light grain and a slight glow at 2, black and white with heavy grain and a strong glow on the last
- [new] Head hit silences every sound at once, then brings it back - user 2026-11-16; picked 2026-11-17: silent 0.3 s, back over 2 s; the game's master volume still to find
- [done] No red marks pointing to whoever hit the player, and no red flash around the edges - marks played 2026-11-08; the flash hidden at the user's ask 2026-11-16, unplayed
- [done] Aiming Overhaul window in DevTools' overlay, values kept in bin\aiming-overhaul.ini - works 2026-11-16
- [done] Gun blur down the iron sights, focused on the front sight - focus fix "perfect" 2026-10-06
- [done] Scope eyepiece: black out-of-focus body over the zoomed view, kicks back per shot, lags behind the look's turn - "its perfect now!" 2026-10-04; lag raised at the user's ask 2026-11-16, unplayed
- [done] Glass inside the eyepiece: lines bow, colours part, rim darkens, faint tint - works 2026-11-16
- [done] New reticles as replaceable PNGs, the holographic chevron lit - "everything works perfectly" 2026-10-09
- [done] Scope surroundings at the unzoomed field of view, no zoom as the scope comes up, easing out of focus over 0.25 s - "everything works perfectly" 2026-10-04; the ease asked 2026-11-16, unplayed
- [done] The eye comes forward while a scope comes up and stays back while it goes down - works 2026-11-16
- [done] No radial edge blur on the scopes the mod draws - works 2026-11-16
- [done] Scope shadow: a soft crescent from the rim as the look turns, slow turns included - user 2026-11-16: "should be emphasized more", raised, unplayed
- [done] Tracers on the belt-fed MGs only, as real belts: 1 in 5 from the M249 and mounted M249/M2, 1 in 4 from the PKM; none from the Dragunov, AS50 or rifles - #57, verified by the user 2026-11-12; own shots included, 1 in 6 ricochet - "All works fine" 2026-10-06
- [done] Tracer colour by cartridge: Soviet rounds (PKM) green, NATO orange-red, the core in the round's colour; AimingOverhaul.SovietTracer weapon data - #57, user 2026-11-12: "Now it's perfect"
- [done] Iron sight zoom switch: off keeps the field of view down iron sights - works 2026-11-16
- [done] No random spread down the AR-16's and MGL-140's scopes - works 2026-11-16
- [done] Scopes as weapon data (AimingOverhaul.Scope* keys), found in the weapon's own mesh - "it seems to be working" 2026-10-08
- [done] Scopes on the Carl Gustaf (its M4 reticle, thick eyecup, no mount below, its barrel blurred and black right of it where the game's shows) and the DLC crossbow (hunting duplex, hold breath, the eyepiece once its sights are up, as the game shows no scope picture for it) (#56) - Carl Gustaf: user 2026-11-13 "the carlg is done"; crossbow: user 2026-11-13 "all is well"
- [done] Weapon and player data as fragments that merge field by field with other mods
- [done] A switch per effect in the Mod Configuration Menu - works 2026-11-16
- [new] Forward/back bob per shot and physical sway in full auto, no climb - user 2026-11-08: "VERY needed"; 2026-11-16: the shake should not pull the gun up but shake it around, maxing out at a very shaky state that lasts to the end of the ammo; picked 2026-11-17: a burst's first 3 shots kick up and stay, from the 4th the climb stops and gun, view and shots shake around the point, worst by the 8th, held until the trigger is let go or the magazine runs dry; the roll goes
- [done] Muzzle light reaches 5 m, two and a half times the game's, from a gamemodesconfig.xml fragment - #67, user 2026-11-12: "perfect!"
- [done] Muzzle flash per gun, first and third person, sized and brightened to its round, barrel and muzzle device, the game's length kept; data only - user 2026-11-12: "they seem to be proper"
- [done] Every gun change reaches the test range's guns too - works 2026-11-16
- [done] Own sound ids moved out of Sound Overhaul's 0x00FC block into 0x00FB, packed from 0x00FB0000: one prefix per mod - user: "changes in instance 3 are verified"
- [done] Drawn through the engine with no Direct3D hooks, so it works under dgVoodoo and DLSS injectors, as Sky Overhaul does - pictures match the old build (fc2drive); the user saw scope, blur and tracers right under dgVoodoo with DLSS 5 #63
- [on hold] Auto lean at corners - shelved until wanted
- [scrapped] Landing: the view dropped a few cm as the knees took it - removed 2026-11-16, user: "its not needed"
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

## To validate

- Standing down the iron sights the front sight wanders 60% further off the mark; crouched as before
- Moving while aiming: crouched sways as much as standing still, standing twice that, and quicker
- Lean, then load a save, get in a vehicle or on a mounted gun: back on foot, upright
- F leaves a vehicle (driver and passenger) and a mounted gun; E no longer does
- F heals a downed buddy; E no longer does
- Dirt in the eye: blobs about half the screen across at the closest, darker, and darker still where two overlap
- A body hit punches the view back 8 cm and down 5°
- No red flash around the edges on a hit
- Scope shadow: shows on a slow turn of a few degrees a second, deep on a fast one
- Scope eyepiece lags behind a turn more and comes back over about a third of a second
- Raising a scope: the view around it eases out of focus over a quarter second, no sudden swap

## Approvals

- played: 7b4c384a 2026-11-14 - "I approve it all changes"
- page text: 10aebf7 2026-10-15 - implied by the aiming-overhaul-1.2.0 release

## Checks

- simplify: ca0a881 2026-11-15 - since aiming-overhaul-1.2.0

## Release order

- after fcse-1.5.0
- after jackall-1.8.0
- after vortex-0.7.0

## Collisions

- vss-vintorez: fixed by b7946eaf - the VSS's Dart Rifle drew the game's scope until it carried AO's scope data (the Dragunov's)
- sky-overhaul: stacks - both hook the engine's draws and EndScene, which FCSE chains; AO's reset seam is the engine's own Reset, not Sky's teardown; the gun blur sits before bloom
- vehicle-overhaul: stacks - both hook the engine's EndScene and Reset, which FCSE chains
- 0x00422510: stacks - the engine device's EndScene, which Sky, Aiming, Vehicle and Inventory Overhaul, Third Person and DevTools hook; FCSE chains them
- 0x00422630: stacks - the engine device's Reset, which Aiming, Vehicle and Inventory Overhaul and DevTools hook; FCSE chains them
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
- difficulty-overhaul: merged by JackAll, no shared field - both edit the guns' entity-library archetypes and particle libraries
- sound-overhaul: merged by JackAll, no shared field, and the shot hooks stack, fixed in Sound Overhaul 1.0.2 - both edit the guns' entity-library archetypes, this mod leaving the echo field as shipped, and both hook the shot at 0x00115680
- legacy-plugin: stacks - both mid-hook the sprint press (0x00144630, Steam); Aiming Overhaul skips it down a scope while the breath is held, Legacy Plugin's sprint toggle only times the keyboard press, and a skipped press leaves no sprint to latch; FCSE stacks same-address hooks
- map-overhaul: merged by JackAll, no shared field - both edit the player archetypes in entitylibrary.fcb (26 files); Aiming Overhaul sets fWalkingAcceleration and fWalkingDeceleration, Map Overhaul clears archGPSVehicleArchetype
- flashlight: merged by JackAll, no shared field - both edit hud.mgb's area #F2CDBA4C; Flashlight adds its own element there, Aiming Overhaul only hides the red flash's placing, and JackAll pairs elements by name
- inventory-overhaul: merged by JackAll, no shared field - both edit hud.mgb's area #F2CDBA4C; Inventory Overhaul adds its own element there, Aiming Overhaul only hides the red flash's placing, and JackAll pairs elements by name
